/* SPDX-License-Identifier: AGPL-3.0-or-later */
#include "teaching-bridge.h"
#include <gio/gio.h>
#include <string.h>
#include <unistd.h>
#define SERVICE "io.github.kaoshou.OpenCam.Teaching"
#define OBJECT_PATH "/io/github/kaoshou/OpenCam/Teaching"
#define INTERFACE SERVICE
#define MAX_MESSAGE 65536u

struct oc_teaching {
    GMainContext *main;
    GMainLoop *loop;
    GThread *thread;
    GCancellable *cancel;
    GDBusConnection *connection;
    char *owner;
    guint subscription;
    oc_teaching_event callback;
    void *user;
    gboolean deferred_free;
};
enum operation_kind { CONNECT,CALL,CLOSE };
struct operation {
    oc_teaching *context;
    enum operation_kind kind;
    const char *method,*json;
    char *output;
    size_t capacity;
    int timeout,result;
    GMutex mutex;
    GCond condition;
    gboolean done;
};
static int failure(oc_teaching *context,GError *error) {
    int result=g_cancellable_is_cancelled(context->cancel)?OC_TEACHING_CANCELLED:OC_TEACHING_UNAVAILABLE;
    if(error)g_error_free(error);
    return result;
}
static char *current_owner(oc_teaching *context) {
    GError *error=NULL;
    GVariant *reply=g_dbus_connection_call_sync(context->connection,"org.freedesktop.DBus","/org/freedesktop/DBus",
        "org.freedesktop.DBus","GetNameOwner",g_variant_new("(s)",SERVICE),G_VARIANT_TYPE("(s)"),
        G_DBUS_CALL_FLAGS_NO_AUTO_START,2000,context->cancel,&error);
    if(!reply){if(error)g_error_free(error);return NULL;}
    const char *value=NULL;g_variant_get(reply,"(&s)",&value);
    char *owner=value&&value[0]==':'?g_strdup(value):NULL;
    g_variant_unref(reply);return owner;
}
static gboolean shell_peer(oc_teaching *context,const char *owner) {
    GError *error=NULL;
    GVariant *reply=g_dbus_connection_call_sync(context->connection,"org.freedesktop.DBus","/org/freedesktop/DBus",
        "org.freedesktop.DBus","GetConnectionCredentials",g_variant_new("(s)",owner),G_VARIANT_TYPE("(a{sv})"),
        G_DBUS_CALL_FLAGS_NO_AUTO_START,2000,context->cancel,&error);
    if(!reply){if(error)g_error_free(error);return FALSE;}
    GVariant *credentials=g_variant_get_child_value(reply,0);
    guint32 uid=0,pid=0;
    gboolean valid=g_variant_lookup(credentials,"UnixUserID","u",&uid)&&uid==(guint32)getuid()&&
        g_variant_lookup(credentials,"ProcessID","u",&pid)&&pid>0&&pid<=2147483647;
    g_variant_unref(credentials);g_variant_unref(reply);
    if(!valid)return FALSE;
    char path[64],executable[4097];
    g_snprintf(path,sizeof path,"/proc/%u/exe",pid);
    ssize_t length=readlink(path,executable,sizeof executable-1);
    if(length<1||length>=(ssize_t)(sizeof executable-1))return FALSE;
    executable[length]='\0';
    return strcmp(executable,"/usr/bin/gnome-shell")==0;
}
static void event_received(GDBusConnection *connection,const gchar *sender,const gchar *path,const gchar *interface,
                           const gchar *signal,GVariant *parameters,gpointer data) {
    (void)connection;(void)path;(void)interface;(void)signal;
    oc_teaching *context=data;
    if(!context->callback||!context->owner||strcmp(sender,context->owner)||
        !g_variant_is_of_type(parameters,G_VARIANT_TYPE("(s)")))return;
    const char *json=NULL;g_variant_get(parameters,"(&s)",&json);
    if(json&&strlen(json)<=MAX_MESSAGE&&g_utf8_validate(json,-1,NULL))context->callback(json,context->user);
}
static int connect_owned(oc_teaching *context) {
    if(g_cancellable_is_cancelled(context->cancel))return OC_TEACHING_CANCELLED;
    if(context->connection)return context->owner?OC_TEACHING_OK:OC_TEACHING_UNAVAILABLE;
    /* Never auto-launch a bus or contact a TCP/remote address. */
    const char *address=g_getenv("DBUS_SESSION_BUS_ADDRESS");
    if(!address||strlen(address)>4096||strchr(address,';')||
        (!g_str_has_prefix(address,"unix:path=")&&!g_str_has_prefix(address,"unix:abstract=")))return OC_TEACHING_UNAVAILABLE;
    GError *error=NULL;
    context->connection=g_dbus_connection_new_for_address_sync(address,
        G_DBUS_CONNECTION_FLAGS_AUTHENTICATION_CLIENT|G_DBUS_CONNECTION_FLAGS_MESSAGE_BUS_CONNECTION,NULL,context->cancel,&error);
    if(!context->connection)return failure(context,error);
    /* Bus/extension loss must never terminate the recording process. */
    g_dbus_connection_set_exit_on_close(context->connection,FALSE);
    char *owner=current_owner(context);
    if(!owner)return failure(context,NULL);
    if(!shell_peer(context,owner)){g_free(owner);return failure(context,NULL);}
    context->owner=owner;
    context->subscription=g_dbus_connection_signal_subscribe(context->connection,owner,INTERFACE,"Event",OBJECT_PATH,NULL,
        G_DBUS_SIGNAL_FLAGS_NONE,event_received,context,NULL);
    return OC_TEACHING_OK;
}
static gboolean known_method(const char *method,gboolean *has_argument) {
    if(!method)return FALSE;
    if(!strcmp(method,"Bind")||!strcmp(method,"Request")){*has_argument=TRUE;return TRUE;}
    *has_argument=FALSE;
    return !strcmp(method,"Capabilities")||!strcmp(method,"Connect")||!strcmp(method,"Challenge")||
        !strcmp(method,"CancelChallenge")||!strcmp(method,"Status");
}
static int call_owned(struct operation *op) {
    oc_teaching *context=op->context;
    if(g_cancellable_is_cancelled(context->cancel))return OC_TEACHING_CANCELLED;
    if(!context->connection||!context->owner)return OC_TEACHING_UNAVAILABLE;
    char *owner=current_owner(context);
    if(!owner)return failure(context,NULL);
    gboolean same=!strcmp(owner,context->owner);g_free(owner);
    if(!same)return OC_TEACHING_OWNER_CHANGED;
    gboolean argument=FALSE;
    if(!known_method(op->method,&argument))return OC_TEACHING_INVALID;
    GError *error=NULL;
    GVariant *reply=g_dbus_connection_call_sync(context->connection,context->owner,OBJECT_PATH,INTERFACE,op->method,
        argument?g_variant_new("(s)",op->json):NULL,G_VARIANT_TYPE("(s)"),G_DBUS_CALL_FLAGS_NO_AUTO_START,
        op->timeout,context->cancel,&error);
    if(!reply)return failure(context,error);
    const char *json=NULL;g_variant_get(reply,"(&s)",&json);
    size_t length=json?strlen(json):0;
    int result=OC_TEACHING_ERROR;
    if(json&&length<=MAX_MESSAGE&&g_utf8_validate(json,(gssize)length,NULL)) {
        if(length>=op->capacity)result=OC_TEACHING_LIMIT;
        else {memcpy(op->output,json,length+1);result=OC_TEACHING_OK;}
    }else if(length>MAX_MESSAGE)result=OC_TEACHING_LIMIT;
    g_variant_unref(reply);return result;
}
static void close_owned(oc_teaching *context) {
    context->callback=NULL;
    if(context->connection){
        if(context->subscription){g_dbus_connection_signal_unsubscribe(context->connection,context->subscription);context->subscription=0;}
        g_dbus_connection_close_sync(context->connection,NULL,NULL);
        g_clear_object(&context->connection);
    }
    g_clear_pointer(&context->owner,g_free);
    g_main_loop_quit(context->loop);
}
static gboolean dispatch_operation(gpointer data) {
    struct operation *op=data;
    if(op->kind==CONNECT)op->result=connect_owned(op->context);
    else if(op->kind==CALL)op->result=call_owned(op);
    else {close_owned(op->context);op->result=OC_TEACHING_OK;}
    g_mutex_lock(&op->mutex);op->done=TRUE;g_cond_signal(&op->condition);g_mutex_unlock(&op->mutex);
    return G_SOURCE_REMOVE;
}
static int execute(oc_teaching *context,struct operation *op) {
    if(!context||g_thread_self()==context->thread)return OC_TEACHING_INVALID;
    op->context=context;op->done=FALSE;g_mutex_init(&op->mutex);g_cond_init(&op->condition);
    GSource *source=g_idle_source_new();g_source_set_priority(source,G_PRIORITY_DEFAULT);
    g_source_set_callback(source,dispatch_operation,op,NULL);g_source_attach(source,context->main);g_source_unref(source);
    g_mutex_lock(&op->mutex);while(!op->done)g_cond_wait(&op->condition,&op->mutex);g_mutex_unlock(&op->mutex);
    int result=op->result;g_cond_clear(&op->condition);g_mutex_clear(&op->mutex);return result;
}
static void free_owned(oc_teaching *context) {
    g_clear_object(&context->cancel);g_main_loop_unref(context->loop);g_main_context_unref(context->main);g_free(context);
}
static gpointer run(gpointer data) {
    oc_teaching *context=data;
    g_main_context_push_thread_default(context->main);g_main_loop_run(context->loop);g_main_context_pop_thread_default(context->main);
    if(context->deferred_free){g_thread_unref(context->thread);free_owned(context);}
    return NULL;
}
oc_teaching *oc_teaching_create(oc_teaching_event callback,void *user) {
    oc_teaching *context=g_new0(oc_teaching,1);
    context->main=g_main_context_new();context->loop=g_main_loop_new(context->main,FALSE);context->cancel=g_cancellable_new();
    context->callback=callback;context->user=user;context->thread=g_thread_new("OpenCam teaching",run,context);return context;
}
int oc_teaching_connect(oc_teaching *context) {
    struct operation op={.kind=CONNECT};return execute(context,&op);
}
int oc_teaching_call(oc_teaching *context,const char *method,const char *json,int timeout_ms,char *output,size_t capacity) {
    if(output&&capacity)output[0]='\0';
    gboolean argument=FALSE;
    if(!context||!output||capacity<2||capacity>MAX_MESSAGE+1||timeout_ms<1||timeout_ms>30000||
        !known_method(method,&argument)||!json||strlen(json)>MAX_MESSAGE||!g_utf8_validate(json,-1,NULL)||(!argument&&json[0]))return OC_TEACHING_INVALID;
    struct operation op={.kind=CALL,.method=method,.json=json,.timeout=timeout_ms,.output=output,.capacity=capacity};
    return execute(context,&op);
}
void oc_teaching_cancel(oc_teaching *context) {if(context)g_cancellable_cancel(context->cancel);}
void oc_teaching_close(oc_teaching *context) {
    if(!context)return;
    oc_teaching_cancel(context);
    if(g_thread_self()==context->thread){context->deferred_free=TRUE;close_owned(context);return;}
    struct operation op={.kind=CLOSE};(void)execute(context,&op);g_thread_join(context->thread);free_owned(context);
}
