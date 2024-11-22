using System;
using System.Runtime.InteropServices;
using Foundation;
using ObjCRuntime;
//[assembly: LinkWith("Approov.xcframework", SmartLink = true, ForceLoad = true, Frameworks = "Foundation")]


namespace ApproovSDK
{
        [Native]
        public enum ApproovTokenFetchStatus : ulong
        {
            Success,
            NoNetwork,
            MITMDetected,
            PoorNetwork,
            NoApproovService,
            BadURL,
            UnknownURL,
            UnprotectedURL,
            NotInitialized,
            Rejected,
            Disabled,
            UnknownKey,
            BadKey,
            BadPayload,
            InternalError
        }
}
