#ifndef MSVC_STDBOOL_H
#define MSVC_STDBOOL_H

#if defined(_MSC_VER) && _MSC_VER >= 1900
#include <stdbool.h>
#elif !defined(__bool_true_false_are_defined)
#define bool unsigned char
#define false 0
#define true 1
#define __bool_true_false_are_defined 1
#endif

#endif
