using Foundation;
namespace ApproovSDK
{

    // @interface ApproovTokenFetchResult : NSObject
    [BaseType(typeof(NSObject))]
    interface ApproovTokenFetchResult
    {
        // @property (readonly) ApproovTokenFetchStatus status;
        [Export("status")]
        ApproovTokenFetchStatus Status();

        // @property (readonly) NSString * _Nonnull token;
        [Export("token")]
        string Token();

        // @property (readonly) NSString * _Nullable secureString;
        [Export("secureString")]
        [return: NullAllowed]
        string SecureString();

        // @property (readonly) NSString * _Nonnull ARC;
        [Export("ARC")]
        string ARC();

        // @property (readonly) NSString * _Nonnull rejectionReasons;
        [Export("rejectionReasons")]
        string RejectionReasons();

        // @property (readonly) BOOL isConfigChanged;
        [Export("isConfigChanged")]
        bool IsConfigChanged();

        // @property (readonly) BOOL isForceApplyPins;
        [Export("isForceApplyPins")]
        bool IsForceApplyPins();

        // @property (readonly) NSData * _Nullable measurementConfig;
        [Export("measurementConfig")]
        [return: NullAllowed]
        NSData MeasurementConfig();

        // -(NSString * _Nonnull)loggableToken;
        [Export("loggableToken")]
        string LoggableToken();

        // @property (readonly) NSString * _Nullable traceID;
        [Export("traceID")]
        [return: NullAllowed]
        string TraceID();
    }

    // typedef void (^ApproovTokenFetchCallback)(ApproovTokenFetchResult * _Nonnull);
    delegate void ApproovTokenFetchCallback(ApproovTokenFetchResult arg0);

    // @interface Approov : NSObject
    [BaseType(typeof(NSObject))]
    interface Approov
    {
        // +(NSString * _Nonnull)stringFromApproovTokenFetchStatus:(ApproovTokenFetchStatus)approovTokenFetchStatus;
        [Static]
        [Export("stringFromApproovTokenFetchStatus:")]
        string StringFromApproovTokenFetchStatus(ApproovTokenFetchStatus approovTokenFetchStatus);

        // +(BOOL)initialize:(NSString * _Nonnull)initialConfig updateConfig:(NSString * _Nullable)updateConfig comment:(NSString * _Nullable)comment error:(NSError * _Nullable * _Nullable)error;
        [Static]
        [Export("initialize:updateConfig:comment:error:")]
        bool Initialize(string initialConfig, [NullAllowed] string updateConfig, [NullAllowed] string comment, [NullAllowed] out NSError error);

        // +(NSString * _Nullable)fetchConfig;
        [Static]
        [Export("fetchConfig")]
        [return: NullAllowed]
        string FetchConfig();

        // +(NSDictionary<NSString *,NSArray<NSString *> *> * _Nullable)getPins:(NSString * _Nonnull)pinType;
        [Static]
        [Export("getPins:")]
        [return: NullAllowed]
        NSDictionary<NSString, NSArray<NSString>> GetPins(string pinType);

        // +(NSString * _Nullable)getPinsJSON:(NSString * _Nonnull)pinType;
        [Static]
        [Export("getPinsJSON:")]
        [return: NullAllowed]
        string GetPinsJSON(string pinType);

        // +(void)fetchApproovToken:(ApproovTokenFetchCallback _Nonnull)callbackHandler :(NSString * _Nonnull)url;
        [Static]
        [Export("fetchApproovToken::")]
        void FetchApproovToken(ApproovTokenFetchCallback callbackHandler, string url);

        // +(ApproovTokenFetchResult * _Nonnull)fetchApproovTokenAndWait:(NSString * _Nonnull)url;
        [Static]
        [Export("fetchApproovTokenAndWait:")]
        ApproovTokenFetchResult FetchApproovTokenAndWait(string url);

        // +(void)fetchCustomJWT:(ApproovTokenFetchCallback _Nonnull)callbackHandler :(NSString * _Nonnull)payload;
        [Static]
        [Export("fetchCustomJWT::")]
        void FetchCustomJWT(ApproovTokenFetchCallback callbackHandler, string payload);

        // +(ApproovTokenFetchResult * _Nonnull)fetchCustomJWTAndWait:(NSString * _Nonnull)payload;
        [Static]
        [Export("fetchCustomJWTAndWait:")]
        ApproovTokenFetchResult FetchCustomJWTAndWait(string payload);

        // +(void)fetchSecureString:(ApproovTokenFetchCallback _Nonnull)callbackHandler :(NSString * _Nonnull)key :(NSString * _Nullable)newDef;
        [Static]
        [Export("fetchSecureString:::")]
        void FetchSecureString(ApproovTokenFetchCallback callbackHandler, string key, [NullAllowed] string newDef);

        // +(ApproovTokenFetchResult * _Nonnull)fetchSecureStringAndWait:(NSString * _Nonnull)key :(NSString * _Nullable)newDef;
        [Static]
        [Export("fetchSecureStringAndWait::")]
        ApproovTokenFetchResult FetchSecureStringAndWait(string key, [NullAllowed] string newDef);

        // +(void)setUserProperty:(NSString * _Nullable)property;
        [Static]
        [Export("setUserProperty:")]
        void SetUserProperty([NullAllowed] string property);

        // +(void)setDataHashInToken:(NSString * _Nonnull)data;
        [Static]
        [Export("setDataHashInToken:")]
        void SetDataHashInToken(string data);

        // +(NSData * _Nullable)getIntegrityMeasurementProof:(NSData * _Nonnull)nonce :(NSData * _Nonnull)measurementConfig;
        [Static]
        [Export("getIntegrityMeasurementProof::")]
        [return: NullAllowed]
        NSData GetIntegrityMeasurementProof(NSData nonce, NSData measurementConfig);

        // +(NSData * _Nullable)getDeviceMeasurementProof:(NSData * _Nonnull)nonce :(NSData * _Nonnull)measurementConfig;
        [Static]
        [Export("getDeviceMeasurementProof::")]
        [return: NullAllowed]
        NSData GetDeviceMeasurementProof(NSData nonce, NSData measurementConfig);

        // +(NSString * _Nullable)getDeviceID;
        [Static]
        [Export("getDeviceID")]
        [return: NullAllowed]
        string DeviceID();

        // +(NSString * _Nullable)getMessageSignature:(NSString * _Nonnull)message;
        [Static]
        [Export("getMessageSignature:")]
        [return: NullAllowed]
        string GetMessageSignature(string message);

        // +(NSString * _Nullable)getAccountMessageSignature:(NSString * _Nonnull)message;
        [Static]
        [Export("getAccountMessageSignature:")]
        [return: NullAllowed]
        string GetAccountMessageSignature(string message);

        // +(void)setDevKey:(NSString * _Nonnull)devKey;
        [Static]
        [Export("setDevKey:")]
        void SetDevKey(string devKey);

        // +(NSString * _Nullable)getInstallMessageSignature:(NSString * _Nonnull)message;
        [Static]
        [Export("getInstallMessageSignature:")]
        [return: NullAllowed]
        string GetInstallMessageSignature(string message);
    }

}
