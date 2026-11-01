/* SPDX-License-Identifier: AGPL-3.0-or-later */
#pragma once
#include <stddef.h>
#ifdef __cplusplus
extern "C" {
#endif
typedef struct oc_teaching oc_teaching;
typedef void (*oc_teaching_event)(const char *json,void *user);
enum { OC_TEACHING_OK=0,OC_TEACHING_UNAVAILABLE=1,OC_TEACHING_CANCELLED=2,
       OC_TEACHING_ERROR=3,OC_TEACHING_LIMIT=4,OC_TEACHING_INVALID=8,OC_TEACHING_OWNER_CHANGED=9 };
/* Calls are serialized by the managed owner; cancel alone is concurrent-safe.
 * Events run on the owned GIO thread. Observers must queue work, not call back. */
oc_teaching *oc_teaching_create(oc_teaching_event callback,void *user);
int oc_teaching_connect(oc_teaching *context);
int oc_teaching_call(oc_teaching *context,const char *method,const char *json,int timeout_ms,char *output,size_t capacity);
void oc_teaching_cancel(oc_teaching *context);
void oc_teaching_close(oc_teaching *context);
#ifdef __cplusplus
}
#endif
