// MIT License
//
// Copyright (c) 2016-present, Critical Blue Ltd.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files
// (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR
// ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH
// THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using static Com.Criticalblue.Approovsdk.Approov;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Java.Lang;

namespace Approov
{
    //[Android.Runtime.Preserve(AllMembers = true)]
    public class ApproovService : ApproovHttpClient
    {
        // Config used to initialize the SDK
        private static string? configUsed = null;

        /*  
        * Initializas the Approov SDK with provided convid
        * string
        */
        public static void Initialize(string config)
        {
            lock (InitializerLock)
            {
                // Check if attempting to use a different config
                if (ApproovSDKInitialized)
                {
                    // Check if attempting to use a different config string
                    if ((configUsed != null) && (configUsed != config))
                    {
                        throw new ConfigurationFailureException(TAG + "Error: SDK already initialized");
                    }
                }
                else
                {
                    // Init the SDK
                    try
                    {
                        Com.Criticalblue.Approovsdk.Approov.Initialize(Android.App.Application.Context, config, "auto", null);
                        ApproovSDKInitialized = true;
                        Console.WriteLine(TAG + "SDK initialized");
                        configUsed = config;
                    }
                    catch (Java.Lang.Exception e)
                    {
                        throw new InitializationFailureException(TAG + "Initialization failed: " + e.Message);
                    }
                    SetUserProperty("approov-service-xamarin");
                }
            }
        }
        /* Creates new Android Approov Service
         */
        protected ApproovService() : this(new HttpClientHandler()) { }

        /* Creates new Android Approov HttpClient
         * @param   custom message handler
         */
        protected ApproovService(HttpMessageHandler handler) : base(handler) { }

        /* Create an ApproovHttpClient instance
         *
         */
        public static ApproovService CreateHttpClient()
        {
            return CreateHttpClient(new HttpClientHandler());
        }

        /* Create an ApproovHttpClient instance
         * @param   custom message handler
         *
         */
        public static ApproovService CreateHttpClient(HttpMessageHandler handler)
        {
            return new ApproovService(handler);
        }

        /*
        *  Allows token prefetch operation to be performed as early as possible. This
        *  permits a token to be available while an application might be loading resources
        *  or is awaiting user input. Since the initial token fetch is the most
        *  expensive the prefetch seems reasonable.
        */
        public static void Prefetch()
        {
            lock (InitializerLock)
            {
                if (ApproovSDKInitialized)
                {
                    _ = HandleTokenFetchAsync();
                }
            }
        }

        private static async Task HandleTokenFetchAsync()
        {
            _ = await Task.Run(() => FetchApproovTokenAndWait("approov.io"));
        }

        /*
         *  Convenience function updating a set of request headers with Approov
         *  It fetches an Approov Token and modifies the message headers
         *  
         */
        protected override HttpRequestMessage UpdateRequestHeadersWithApproov(HttpRequestMessage message)
        {
            // Check if we have initialized the SDK
            lock (InitializerLock)
            {
                if (!ApproovSDKInitialized) return message;
            }
            // The return value
            HttpRequestMessage returnMessage = message;
            // Build the final url (we have to use to call FetchApproovToken)
            string urlWithBaseAddress;
            if ((BaseAddress != null) && (message.RequestUri != null))
                urlWithBaseAddress = new Uri(BaseAddress, message.RequestUri).ToString();
            else if (BaseAddress != null)
                urlWithBaseAddress = BaseAddress.ToString();
            else if (message.RequestUri != null)
                urlWithBaseAddress = message.RequestUri.ToString();
            else
                throw new ApproovException(TAG + " Must set host in either ApproovHttpCLient.BaseAddress or GET/POST/PUT method");

            // Check if the URL matches one of the exclusion regexs and just return if it does
            if (CheckURLIsExcluded(urlWithBaseAddress))
            {
                Console.WriteLine(TAG + "UpdateRequestHeadersWithApproov excluded url " + urlWithBaseAddress);
                return returnMessage;
            }

            // The Request Headers to check/modify.
            using var tempMessage = new HttpRequestMessage();
            HttpRequestHeaders headersToCheck = tempMessage.Headers;
            foreach (KeyValuePair<string, IEnumerable<string>> entry in message.Headers)
            {
                headersToCheck.TryAddWithoutValidation(entry.Key, entry.Value);
            }
            // Now also copy the DefaultHeaders since we might want to bind to a value in them
            foreach (KeyValuePair<string, IEnumerable<string>> entry in DefaultRequestHeaders)
            {
                headersToCheck.TryAddWithoutValidation(entry.Key, entry.Value);
            }

            // Check if Bind Header is set to a non empty String
            lock (BindingHeaderLock)
            {
                if (BindingHeader != null)
                {
                    if (headersToCheck.Contains(BindingHeader))
                    {
                        // Returns all header values for a specified header stored in the HttpHeaders collection.
                        var headerValues = headersToCheck.GetValues(BindingHeader);
                        var enumerator = headerValues.GetEnumerator();
                        int i = 0;
                        string? headerValue = null;
                        while (enumerator.MoveNext())
                        {
                            i++;
                            headerValue = enumerator.Current;
                        }
                        // Check that we have only one value
                        if (i != 1)
                        {
                            throw new ConfigurationFailureException(TAG + "Only one value can be used as binding header, detected " + i);
                        }
                        if (headerValue != null)
                        {
                            SetDataHashInToken(headerValue);
                            // Log
                            Console.WriteLine(TAG + "bindheader set: " + headerValue);
                        }
                        else
                        {
                            throw new ConfigurationFailureException(TAG + "Binding header value is null: " + BindingHeader);
                        }
                        SetDataHashInToken(headerValue);
                        // Log
                        Console.WriteLine(TAG + "bindheader set: " + headerValue);
                    }
                    else
                    {
                        throw new ConfigurationFailureException(TAG + "Missing token binding header: " + BindingHeader);
                    }
                }
            }// lock

            // Invoke fetch token sync
            var approovResult = FetchApproovTokenAndWait(urlWithBaseAddress);

            // Log result
            if (approovResult == null)
            {
                Console.WriteLine(TAG + "Approov token for " + urlWithBaseAddress + " : " + "null");
                // We throw at this point since we need to handle the result and we can not continue: the JNI call has failed
                throw new PermanentException(TAG + "Approov JNI call failed during FetchApproovTokenAndWait(" + urlWithBaseAddress +")");
            }
            else
            {
                Console.WriteLine(TAG + "Approov token for " + urlWithBaseAddress + " : " + approovResult.LoggableToken);
            }

            // Check the status of the Approov token fetch
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            if (approovResult.Status == TokenFetchStatus.Success)
            {
                // we successfully obtained a token so add it to the header for the HttpClient or HttpRequestMessage
                // Check if the request headers already contains an ApproovTokenHeader (from previous request, etc)
                if (returnMessage.Headers.Contains(ApproovTokenHeader))
                {
                    if (!returnMessage.Headers.Remove(ApproovTokenHeader))
                    {
                        // We could not remove the original header
                        throw new ApproovException(TAG + "Failed removing header: " + ApproovTokenHeader);
                    }
                }
                returnMessage.Headers.TryAddWithoutValidation(ApproovTokenHeader, ApproovTokenPrefix + approovResult.Token);
            }
            else if ((approovResult.Status == TokenFetchStatus.NoNetwork) ||
                   (approovResult.Status == TokenFetchStatus.PoorNetwork) ||
                   (approovResult.Status == TokenFetchStatus.MitmDetected))
            {
                /* We are unable to get the approov token due to network conditions so the request can
                *  be retried by the user later
                */
                if (!ProceedOnNetworkFail)
                {
                    // Must not proceed with network request and inform user a retry is needed
                    throw new NetworkingErrorException(TAG + "Retry attempt needed. " + approovResult.LoggableToken, true);
                }
            }
            else if ((approovResult.Status == TokenFetchStatus.UnknownUrl) ||
                 (approovResult.Status == TokenFetchStatus.UnprotectedUrl) ||
                 (approovResult.Status == TokenFetchStatus.NoApproovService))
            {
                Console.WriteLine(TAG + "Will continue without Approov-Token");
            }
            else
            {
                throw new PermanentException("Unknown approov token fetch result " + approovResult.Status);
            }
#pragma warning restore CS8602 // Dereference of a possibly null reference.

            /* We only continue additional processing if we had a valid status from Approov, to prevent additional delays
             * by trying to fetch from Approov again and this also protects against header substitutions in domains not
             * protected by Approov and therefore are potentially subject to a MitM.
             */
            if ((approovResult.Status != TokenFetchStatus.Success) &&
                (approovResult.Status != TokenFetchStatus.UnprotectedUrl))
            {
                // We return the unmodified message
                return returnMessage;
            }

            /* We now have to deal with any substitution headers */
            // Make a copy of original dictionary
            Dictionary<string, string> originalSubstitutionHeaders;
            lock (SubstitutionHeadersLock)
            {
                originalSubstitutionHeaders = new Dictionary<string, string>(SubstitutionHeaders);
            }
            // Iterate over the copied dictionary
            foreach (KeyValuePair<string, string> entry in originalSubstitutionHeaders)
            {
                string header = entry.Key;
                string prefix = entry.Value; // can be null
                // Check if prefix for a given header is not null
                if (prefix == null) prefix = "";
                string? value = null;
                if (headersToCheck.TryGetValues(header, out IEnumerable<string> values))
                {
                    value = values.First();
                }
                // The request headers do NOT contain the header needing replaced
                if (value == null) continue;    // None of the available headers contain the value
                // Check if the request contains the header we want to replace
                if (value.StartsWith(prefix) && (value.Length > prefix.Length))
                {
                    string stringValue = value.Substring(prefix.Length);
                    var approovResults = FetchSecureStringAndWait(stringValue, null);
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                    Console.WriteLine(TAG + "Substituting header: " + header + ", " + approovResults.Status.ToString());
#pragma warning restore CS8602 // Dereference of a possibly null reference.
                    // Process the result of the token fetch operation
                    if (approovResults.Status == TokenFetchStatus.Success)
                    {
                        // We add the modified header to the request after removing duplicate
                        if (approovResults.SecureString != null)
                        {
                            if (returnMessage.Headers.Contains(header))
                            {
                                if (!returnMessage.Headers.Remove(header))
                                {
                                    // We could not remove the original header
                                    throw new ApproovException(TAG + "Failed removing header: " + header);
                                }
                            }
                            returnMessage.Headers.TryAddWithoutValidation(header, prefix + approovResults.SecureString);
                        }
                        else
                        {
                            // Secure string is null
                            throw new ApproovException(TAG + "UpdateRequestHeadersWithApproov null return from secure message fetch");
                        }
                    }
                    else if (approovResults.Status == TokenFetchStatus.Rejected)
                    {
                        // if the request is rejected then we provide a special exception with additional information
                        string localARC = approovResults.ARC ?? string.Empty;
                        string localReasons = approovResults.RejectionReasons ?? string.Empty;
                        throw new RejectionException(TAG + "secure message rejected", arc: localARC, rejectionReasons: localReasons);
                    }
                    else if (approovResults.Status == TokenFetchStatus.NoNetwork ||
                            approovResults.Status == TokenFetchStatus.PoorNetwork ||
                            approovResults.Status == TokenFetchStatus.MitmDetected)
                    {
                        /* We are unable to get the secure string due to network conditions so the request can
                        *  be retried by the user later
                        *  We are unable to get the secure string due to network conditions, so - unless this is
                        *  overridden - we must not proceed. The request can be retried by the user later.
                        */
                        if (!ProceedOnNetworkFail)
                        {
                            // We throw
                            throw new NetworkingErrorException(TAG + "Header substitution: network issue, retry needed");
                        }
                    }
                    else if (approovResults.Status != TokenFetchStatus.UnknownKey)
                    {
                        // we have failed to get a secure string with a more serious permanent error
                        throw new PermanentException(TAG + "Header substitution: " + approovResults.Status.ToString());
                    }
                } // if (value.StartsWith ...
            }
            //end
            /* Finally, we deal with any query parameter substitutions, which may require further fetches but these
             * should be using cached results */
            // Make a copy of original substitutionQuery set
            HashSet<string> originalQueryParams;
            lock (SubstitutionQueryParamsLock)
            {
                originalQueryParams = new HashSet<string>(SubstitutionQueryParams);
            }
            string urlString = urlWithBaseAddress;
            foreach (string entry in originalQueryParams)
            {
                string pattern = entry;
                Regex regex = new Regex(pattern, RegexOptions.ECMAScript);
                // See if there is any match
                MatchCollection matchedPatterns = regex.Matches(urlString);
                // We skip Group at index 0 as this is the match (e.g. ?Api-Key=api_key_placeholder) for the whole
                // regex, but we only want to replace the query parameter value part (e.g. api_key_placeholder)
                for (int count = 0; count < matchedPatterns.Count; count++)
                {
                    // We must have 2 Groups, the first being the full pattern and the second one the query parameter
                    if (matchedPatterns[count].Groups.Count != 2) continue;
                    string matchedText = matchedPatterns[count].Groups[1].Value;
                    var approovResults = FetchSecureStringAndWait(matchedText, null);
#pragma warning disable CS8602 // Dereference of a possibly null reference.
                    if (approovResults.Status == TokenFetchStatus.Success)
                    {
                        // Replace the ocureences and modify the URL
                        string newURL = urlString.Replace(matchedText, approovResults.SecureString);
                        // we log
                        Console.WriteLine(TAG + "replacing url with " + newURL);
                        returnMessage.RequestUri = new Uri(newURL);
                    }
                    else if (approovResults.Status == TokenFetchStatus.Rejected)
                    {
                        // if the request is rejected then we provide a special exception with additional information
                        string localARC = approovResults.ARC ?? string.Empty;
                        string localReasons = approovResults.RejectionReasons ?? string.Empty;
                        throw new RejectionException(TAG + "UpdateRequestHeadersWithApproov secure message rejected", arc: localARC, rejectionReasons: localReasons);
                    }
                    else if (approovResults.Status == TokenFetchStatus.NoNetwork ||
                            approovResults.Status == TokenFetchStatus.PoorNetwork ||
                            approovResults.Status == TokenFetchStatus.MitmDetected)
                    {
                        /* We are unable to get the secure string due to network conditions so the request can
                        *  be retried by the user later
                        *  We are unable to get the secure string due to network conditions, so - unless this is
                        *  overridden - we must not proceed. The request can be retried by the user later.
                        */
                        if (!ProceedOnNetworkFail)
                        {
                            // We throw
                            throw new NetworkingErrorException(TAG + "Query parameter substitution: network issue, retry needed");
                        }
                    }
                    else if (approovResults.Status != TokenFetchStatus.UnknownKey)
                    {
                        // we have failed to get a secure string with a more serious permanent error
                        throw new PermanentException(TAG + "Query parameter substitution error: " + approovResults.Status.ToString());
                    }
#pragma warning restore CS8602 // Dereference of a possibly null reference.
                }
            }// foreach
            // We return the new message
            return returnMessage;

        }//UpdateRequestHeadersWithApproov

        /*
         * Fetches a secure string with the given key. If newDef is not nil then a secure string for
         * the particular app instance may be defined. In this case the new value is returned as the
         * secure string. Use of an empty string for newDef removes the string entry. Note that this
         * call may require network transaction and thus may block for some time, so should not be called
         * from the UI thread. If the attestation fails for any reason then an exception is raised. Note
         * that the returned string should NEVER be cached by your app, you should call this function when
         * it is needed. If the fetch fails for any reason an exception is thrown with description. Exceptions
         * could be due to the feature not being enabled from the CLI tools ...
         *
         * @param key is the secure string key to be looked up
         * @param newDef is any new definition for the secure string, or nil for lookup only
         * @return secure string (should not be cached by your app) or nil if it was not defined or an error ocurred
         * @throws exception with description of cause
         */
        public static string FetchSecureString(string key, string newDef)
        {
            string type = "lookup";
            if (newDef != null)
            {
                type = "definition";
            }
            // Invoke fetchSecureString
            var approovResults = FetchSecureStringAndWait(key, newDef);
            // We have throw at this point if the JNI call has failed
            if (approovResults == null)
            {
                throw new PermanentException(TAG + "FetchSecureString: JNI call failed");
            }
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            Console.WriteLine(TAG + "FetchSecureString: " + type + " " + approovResults.Status.ToString());
#pragma warning restore CS8602 // Dereference of a possibly null reference.
            if (approovResults.Status == TokenFetchStatus.Disabled)
            {
                throw new ConfigurationFailureException(TAG + "FetchSecureString:  secure message string feature is disabled");
            }
            else if (approovResults.Status == TokenFetchStatus.UnknownKey)
            {
                throw new ConfigurationFailureException(TAG + "FetchSecureString: secure string unknown key");
            }
            else if (approovResults.Status == TokenFetchStatus.Rejected)
            {
                // if the request is rejected then we provide a special exception with additional information
#pragma warning disable CS8600 // Converting null literal or possible null value to non-nullable type.
                string localARC = approovResults.ARC;
                string localReasons = approovResults.RejectionReasons;
#pragma warning restore CS8600 // Converting null literal or possible null value to non-nullable type.
                throw new RejectionException(TAG + "FetchSecureString: secure message rejected", arc: localARC ?? string.Empty, rejectionReasons: localReasons ?? string.Empty);
            }
            else if (approovResults.Status == TokenFetchStatus.NoNetwork ||
                    approovResults.Status == TokenFetchStatus.PoorNetwork ||
                    approovResults.Status == TokenFetchStatus.MitmDetected)
            {
                /* We are unable to get the secure string due to network conditions so the request can
                *  be retried by the user later
                *  We are unable to get the secure string due to network conditions, so we must not proceed. The request can be retried by the user later.
                */

                // We throw
                throw new NetworkingErrorException(TAG + "FetchSecureString: network issue, retry needed");

            }
            else if ((approovResults.Status != TokenFetchStatus.Success) &&
                    approovResults.Status != TokenFetchStatus.UnknownKey)
            {
                // we have failed to get a secure string with a more serious permanent error
                throw new PermanentException(TAG + "FetchSecureString: " + approovResults.Status.ToString());
            }
            // We throw if JNI call has failed
            if (approovResults.SecureString == null)
            {
                throw new PermanentException(TAG + "FetchSecureString: secure string is null");
            }
            return approovResults.SecureString;
        }

        /*
         * Fetches a custom JWT with the given payload. Note that this call will require network
         * transaction and thus will block for some time, so should not be called from the UI thread.
         * If the fetch fails for any reason an exception will be thrown. Exceptions could be due to
         * malformed JSON string provided ...
         *
         * @param payload is the marshaled JSON object for the claims to be included
         * @return custom JWT string or nil if an error occurred
         * @throws exception with description of cause
         */
        public static string FetchCustomJWT(string payload)
        {
            TokenFetchResult? approovResult;
            try
            {
                approovResult = FetchCustomJWTAndWait(payload);
                // Throw if JNI call has failed
                if (approovResult == null)
                {
                    throw new PermanentException(TAG + "FetchCustomJWT: JNI call failed");
                }
                // Throw if JNI call has failed
                if (approovResult.Status == null)
                {
                    throw new PermanentException(TAG + "FetchCustomJWT: JNI call failed");
                }
                Console.WriteLine(TAG + "FetchCustomJWT: " + approovResult.Status.ToString());
                // process the returned Approov status
            }
            catch (IllegalArgumentException e)
            {
                throw new PermanentException(TAG + "FetchCustomJWT: malformed JSON " + e.Message);
            }

            if (approovResult.Status == TokenFetchStatus.Disabled)
            {
                throw new ConfigurationFailureException(TAG + "FetchCustomJWT: feature not enabled");
            }
            else if (approovResult.Status == TokenFetchStatus.Rejected)
            {
                string localARC = approovResult.ARC ?? string.Empty;
                string localReasons = approovResult.RejectionReasons ?? string.Empty;
                // if the request is rejected then we provide a special exception with additional information
                throw new RejectionException(TAG + "FetchCustomJWT: rejected", arc: localARC, rejectionReasons: localReasons);
            }
            else if (approovResult.Status == TokenFetchStatus.NoNetwork ||
                  approovResult.Status == TokenFetchStatus.PoorNetwork ||
                  approovResult.Status == TokenFetchStatus.MitmDetected)
            {
                /* We are unable to get the secure string due to network conditions so the request can
                *  be retried by the user later
                *  We are unable to get the secure string due to network conditions, so we must not proceed. The request can be retried by the user later.
                */
                // We throw
                throw new NetworkingErrorException(TAG + "FetchCustomJWT: network issue, retry needed");

            }
            else if (approovResult.Status != TokenFetchStatus.Success)
            {
                throw new PermanentException(TAG + "FetchCustomJWT: " + approovResult.Status.ToString());
            }
            // Throw if JNI call fails
            if (approovResult.Token == null)
            {
                throw new PermanentException(TAG + "FetchCustomJWT: JWT token is null");
            }
            return approovResult.Token;
        }

        /*
         * Performs a precheck to determine if the app will pass attestation. This requires secure
         * strings to be enabled for the account, although no strings need to be set up. This will
         * likely require network access so may take some time to complete. It may throw an exception
         * if the precheck fails or if there is some other problem. Exceptions could be due to
         * a rejection .......
         */
        public static void Precheck()
        {
            TokenFetchResult? approovResult = FetchSecureStringAndWait("precheck-dummy-key", null);
            // Throw if JNI call fails
            if (approovResult == null)
            {
                throw new PermanentException(TAG + "Precheck: JNI call failed");
            }
            else if (approovResult.Status == null) {
                throw new PermanentException(TAG + "Precheck approovResult: JNI call failed");
            }
            // Process the result
            if (approovResult.Status == TokenFetchStatus.Rejected)
            {
                string localARC = approovResult.ARC ?? string.Empty;
                string localReasons = approovResult.RejectionReasons ?? string.Empty;
                throw new RejectionException(TAG + "Precheck: rejected ", arc: localARC, rejectionReasons: localReasons);
            }
            else if (approovResult.Status == TokenFetchStatus.NoNetwork ||
                approovResult.Status == TokenFetchStatus.PoorNetwork ||
                approovResult.Status == TokenFetchStatus.MitmDetected)
            {
                throw new NetworkingErrorException(TAG + "Precheck: network issue, retry needed");
            }
            else if ((approovResult.Status != TokenFetchStatus.Success) &&
                  approovResult.Status != TokenFetchStatus.UnknownKey)
            {
                throw new PermanentException(TAG + "Precheck: " + approovResult.Status.ToString() ??  string.Empty);
            }
            Console.WriteLine(TAG + "Precheck " + approovResult.LoggableToken);
        }

        /**
         * Gets the device ID used by Approov to identify the particular device that the SDK is running on. Note
         * that different Approov apps on the same device will return a different ID. Moreover, the ID may be
         * changed by an uninstall and reinstall of the app.
         *
         * @return String of the device ID or null in case of an error
         */
        public static string GetDeviceID()
        {
            string? deviceID = DeviceID;
            if (deviceID == null)
            {
                Console.WriteLine(TAG + "DeviceID: " + deviceID ?? "null");
                throw new PermanentException(TAG + "DeviceID: null");
            }
            return deviceID;
        }

        /**
         * Directly sets the data hash to be included in subsequently fetched Approov tokens. If the hash is
         * different from any previously set value then this will cause the next token fetch operation to
         * fetch a new token with the correct payload data hash. The hash appears in the
         * 'pay' claim of the Approov token as a base64 encoded string of the SHA256 hash of the
         * data. Note that the data is hashed locally and never sent to the Approov cloud service.
         *
         * @param data is the data to be hashed and set in the token
         */
        public static void SetDataHashInToken(string data)
        {
            Console.WriteLine(TAG + "SetDataHashInToken");
            Com.Criticalblue.Approovsdk.Approov.SetDataHashInToken(data);
        }


        /**
         * Gets the signature for the given message. This uses an account specific message signing key that is
         * transmitted to the SDK after a successful fetch if the facility is enabled for the account. Note
         * that if the attestation failed then the signing key provided is actually random so that the
         * signature will be incorrect. An Approov token should always be included in the message
         * being signed and sent alongside this signature to prevent replay attacks. If no signature is
         * available, because there has been no prior fetch or the feature is not enabled, then an
         * ApproovException is thrown.
         *
         * @param message is the message whose content is to be signed
         * @return String of the base64 encoded message signature or null if no message signing key is available
         */
        public static string? GetMessageSignature(string message)
        {
            var signature = Com.Criticalblue.Approovsdk.Approov.GetMessageSignature(message);
            Console.WriteLine(TAG + "GetMessageSignature");
            return signature;
        }

        /**
         * Performs an Approov token fetch for the given URL. This should be used in situations where it
         * is not possible to use the networking interception to add the token. This will
         * likely require network access so may take some time to complete. If the attestation fails
         * for any reason then an Exception is thrown. ... Note that
         * the returned token should NEVER be cached by your app, you should call this function when
         * it is needed.
         *
         * @param url is the URL giving the domain for the token fetch
         * @return String of the fetched token
         * @throws Exception if there was a problem
         */

        public static string FetchToken(string url)
        {
            var approovResult = FetchApproovTokenAndWait(url);
            if (approovResult == null)
            {
                throw new PermanentException(TAG + "FetchToken: JNI call failed");
            } else if (approovResult.Status == null)
            {
                throw new PermanentException(TAG + "FetchToken.approovResult: JNI call failed");
            }
            Console.WriteLine(TAG + "FetchToken: " + url + " " + approovResult.Status.ToString());

            // Process the result
            if (approovResult.Status == TokenFetchStatus.Success)
            {
                //.Throw if JNI call fails
                if (approovResult.Token == null)
                {
                    throw new PermanentException(TAG + "FetchToken.Token: token is null");
                }
                return approovResult.Token;
            }
            else if (approovResult.Status == TokenFetchStatus.NoNetwork ||
              approovResult.Status == TokenFetchStatus.PoorNetwork ||
              approovResult.Status == TokenFetchStatus.MitmDetected)
            {
                throw new NetworkingErrorException(TAG + "FetchToken: networking error, retry needed");
            }
            else
            {
                throw new PermanentException(TAG + "FetchToken: " + approovResult.Status.ToString());
            }
        }

        /*  Get set of pins from Approov SDK in JSON format
         *
         *
         */
        public static string? GetPinsJSON(string pinType = "public-key-sha256")
        {
            return Com.Criticalblue.Approovsdk.Approov.GetPinsJSON(pinType);
        }

        /*  Set development key for Approov SDK in order to force an app to pass attestation
         *  This can be used if an app has to be resigned in a test envoironment and would thus fail
         *  attestation. This is a development feature and should not be used in production.
         */
        public static void SetDevKey(string devKey) { 
            Com.Criticalblue.Approovsdk.Approov.SetDevKey(devKey);
        }


        /* TLS hanshake callback */

        /*  Extract a public key from certificate and append to a header specific to the key type
         *  Returns nil if the key type in the certificate can not be recognized/extracted
         */
        public byte[]? PublicKeyWithHeader(X509Certificate2 cert)
        {
            try
            {
                // Attempt to get the public key as an AsymmetricAlgorithm
                using (AsymmetricAlgorithm? publicKey = cert.GetRSAPublicKey() ?? (AsymmetricAlgorithm?)cert.GetECDsaPublicKey())
                {
                    if (publicKey != null)
                    {
                        // Export the public key in X.509 SubjectPublicKeyInfo format
                        return publicKey.ExportSubjectPublicKeyInfo();
                    }
                }
            }
            catch (CryptographicException e)
            {
                Console.WriteLine(TAG + "Unable to extract public key: " + e.Message);
            }

            // Return null if the key extraction fails
            return null;
        }

        /*  TLS handshake inspection callback
         *
         */
        protected override bool ServerCallback(HttpRequestMessage sender, X509Certificate2 cert, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {

            if (sslPolicyErrors != SslPolicyErrors.None)
                return false;

            try
            {
                _ = chain.Build(cert);
            }
            catch (System.Exception e)
            {
                throw new ApproovException(TAG + "Exception attempting to build certificate chain: " + e.Message);
            }

            if (chain.ChainElements.Count == 0)
                Console.WriteLine(TAG + "Empty certificate chain from callback function.");
            JObject allPins;
            // 1. Get Approov pins
            var aString = GetPinsJSON();
            // Throw if the undrlying JNI call fails
            if (aString == null)
            {
                throw new PermanentException(TAG + "Unable to obtain pins from SDK: GetPinsJSON JNI call failed");
            }
            // Convert the JSON String to its object representation
            try
            {
                JObject jsonContents = JObject.Parse(aString);
                allPins = jsonContents;
            }
            catch (JsonReaderException e)
            {
                throw new ApproovException(TAG + "Unable to obtain pins from SDK " + e.Message);
            }

            //IDictionary<string, IList<string>> allPins = (IDictionary<string, IList<string>>)GetPins(kShaTypeString);
            if (allPins == null)
            {
                throw new ApproovException(TAG + "Unable to obtain pins from SDK");
            }

            // 2. Get hostname => sender.RequestUri
            string hostname = sender.RequestUri?.Host ?? throw new ApproovException(TAG + "RequestUri is null");
            JArray? allPinsForHost = allPins[hostname] as JArray;
            // if there are no pins for the domain (but the host is present) then use any managed trust roots instead
            if ((allPinsForHost != null) && (allPinsForHost.Count == 0))
            {
                allPinsForHost = allPins["*"] as JArray;
            }
            // if we are not pinning then we consider this level of trust to be acceptable
            if ((allPinsForHost == null) || (allPinsForHost.Count == 0))
            {
                // 3. Host is not being pinned and we have succesfully checked certificate chain
                Console.WriteLine(TAG + "Host not pinned " + hostname);
                return true;
            }


            // 3. Iterate over certificate chain and attempt to match PK pin to one in Approov SDK
            foreach (X509ChainElement element in chain.ChainElements)
            {
                var certificate = element.Certificate;

                byte[]? pkiBytes = PublicKeyWithHeader(certificate);
                // If we fail to extract pins from certificate, we can not pin; log error and allow connection
                // since we have already verified the certificate as valid
                if (pkiBytes == null)
                {
                    SHA256 certHash = SHA256.Create();
                    byte[] certHashBytes = certHash.ComputeHash(certificate.RawData);
                    string certHashBase64 = Convert.ToBase64String(certHashBytes);
                    Console.WriteLine(TAG + " Failed to extract Public Key from certificate for host " + hostname + ". Cert hash: " + certHashBase64);
                }
                else
                {
                    SHA256 hash = SHA256.Create();
                    byte[] hashBytes = hash.ComputeHash(pkiBytes);
                    string publicKeyBase64 = Convert.ToBase64String(hashBytes);

                    // Iterate over the list of pins and test each one
                    foreach (string? entry in allPinsForHost)
                    {
                        if(entry == null) continue;
                        if (entry.Equals(publicKeyBase64))
                        {
                            Console.WriteLine(TAG + hostname + " Matched public key pin " + publicKeyBase64 + " from " + allPinsForHost.Count + " pins");
                            return true;
                        }
                    }
                }
            }
            // 5. No pins match
            Console.WriteLine(TAG + hostname + " No matching public key pins from " + allPinsForHost.Count + " pins");
            return false;

        }

    } // ApproovService



}