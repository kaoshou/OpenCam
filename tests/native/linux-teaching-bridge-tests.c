// SPDX-License-Identifier: AGPL-3.0-or-later
#include "../../src/ScreenRecorder.Platform.Linux/Native/teaching-bridge.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <stdatomic.h>
#define CHECK(value) do { if (!(value)) { fprintf(stderr,"teaching bridge check failed at %d\n",__LINE__); return 1; } } while (0)
static atomic_int events;
static void received(const char *json,void *user) {
    (void)user;
    if(json&&strstr(json,"\"event\":\"probe\""))atomic_fetch_add(&events,1);
}
int main(int argc,char **argv) {
    char output[65537];
    CHECK(oc_teaching_connect(NULL)==OC_TEACHING_INVALID);
    CHECK(oc_teaching_call(NULL,"Eval","",1000,output,sizeof output)==OC_TEACHING_INVALID);
    for(int i=0;i<10;i++) {
        oc_teaching *context=oc_teaching_create(NULL,NULL);CHECK(context);
        memset(output,'x',sizeof output);
        CHECK(oc_teaching_call(context,"Eval","",1000,output,sizeof output)==OC_TEACHING_INVALID);
        CHECK(output[0]=='\0');CHECK(output[sizeof output-1]=='x');
        oc_teaching_cancel(context);
        CHECK(oc_teaching_connect(context)==OC_TEACHING_CANCELLED);
        oc_teaching_close(context);
    }
    if(argc==2&&strcmp(argv[1],"connected")==0) {
        oc_teaching *context=oc_teaching_create(received,NULL);CHECK(context);
        CHECK(oc_teaching_connect(context)==OC_TEACHING_OK);
        CHECK(oc_teaching_call(context,"Capabilities","",2000,output,sizeof output)==OC_TEACHING_OK);
        CHECK(strstr(output,"\"protocol\":1"));
        char small[4]={'x','x','x','x'};
        CHECK(oc_teaching_call(context,"Capabilities","",2000,small,sizeof small)==OC_TEACHING_LIMIT);
        CHECK(small[0]=='\0');CHECK(small[3]=='x');
        CHECK(oc_teaching_call(context,"Status","",2000,output,sizeof output)==OC_TEACHING_OK);
        for(int i=0;i<100&&atomic_load(&events)==0;i++)usleep(10000);
        CHECK(atomic_load(&events)==1);
        oc_teaching_cancel(context);
        CHECK(oc_teaching_call(context,"Capabilities","",2000,output,sizeof output)==OC_TEACHING_CANCELLED);
        oc_teaching_close(context);
    }
    puts("owned teaching bridge checks passed");
    return 0;
}
