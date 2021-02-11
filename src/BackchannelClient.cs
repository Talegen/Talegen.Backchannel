/*
 *
 * (c) Copyright Talegen, LLC.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * http://www.apache.org/licenses/LICENSE-2.0
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
*/

namespace Talegen.Backchannel
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Cache;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using IdentityModel.Client;
    using Newtonsoft.Json;
    using Talegen.Backchannel.Models;
    using Talegen.Backchannel.Properties;
    using Talegen.Common.Core.Extensions;

    /// <summary>
    /// This class is used to provide a basic backchannel for API to API interactions within a micro-service infrastructure.
    /// </summary>
    public class BackchannelClient
    {
        #region Private Constants

        /// <summary>
        /// The custom Bastille.Id delegation grant type name
        /// </summary>
        private const string DelegationGrantTypeName = "delegation";

        #endregion Private Constants

        #region Private Fields

        /// <summary>
        /// Contains the calculated default token endpoint.
        /// </summary>
        private readonly string defaultTokenEndpoint;

        /// <summary>
        /// Contains a value indicating whether the class has been disposed.
        /// </summary>
        private bool disposed;

        /// <summary>
        /// Contains a discovery result from the authority.
        /// </summary>
        private DiscoveryDocumentResponse discovery;

        #endregion Private Fields

        #region Public Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="BackchannelClient" /> class.
        /// </summary>
        /// <param name="config">Contains the configuration of the Backchannel client.</param>
        public BackchannelClient(BackchannelConfig config)
        {
            this.Configuration = config ?? throw new ArgumentNullException(nameof(config));
            this.defaultTokenEndpoint = new Uri(this.Configuration.AuthorityUri, "/connect/token").ToString();
        }

        #endregion Public Constructors

        #region public Properties

        /// <summary>
        /// Gets the configuration.
        /// </summary>
        /// <value>The configuration.</value>
        public BackchannelConfig Configuration { get; }

        /// <summary>
        /// Gets a value indicating whether the client has authenticated with the server at some point
        /// </summary>
        public bool HasAuthenticated => this.TokenResponse != null && !this.TokenResponse.IsError;

        /// <summary>
        /// Gets the last error response model from an internal RequestContent call.
        /// </summary>
        public BackchannelErrorResponseModel LastErrorResponse { get; private set; }

        /// <summary>
        /// Gets the last <see cref="Exception" /> handled within the client.
        /// </summary>
        public Exception LastException { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the client has a recent error.
        /// </summary>
        public bool HasErrors => this.LastException != null || (this.LastErrorResponse != null && this.LastErrorResponse.Messages.Any(e => e.ErrorType == ErrorType.Fatal || e.ErrorType == ErrorType.Critical));

        #endregion public Properties

        #region Protected Properties

        /// <summary>
        /// Gets or sets the client authentication token response.
        /// </summary>
        protected TokenResponse TokenResponse { get; set; }

        /// <summary>
        /// Gets the value of the access token.
        /// </summary>
        protected string AccessToken => this.TokenResponse?.AccessToken ?? string.Empty;

        /// <summary>
        /// Gets the token endpoint for the identity authority.
        /// </summary>
        protected string TokenEndpoint => this.Configuration.UseDiscoveryForEndpoint && this.discovery != null ? this.discovery.TokenEndpoint : this.defaultTokenEndpoint;

        #endregion

        #region Public Methods

        /// <summary>
        /// Authenticates the client with the specified scopes in a synchronous manor.
        /// </summary>
        /// <param name="scopes">Contains the scopes that are requested for the client credentials authentication.</param>
        /// <example>
        /// <code>
        ///BackchannelClient client = new BackchannelClient(config)
        ///client.Authenticate();
        /// </code>
        /// </example>
        /// <returns>Returns the result of the authentication.</returns>
        public virtual bool Authenticate(string scopes = "")
        {
            return AsyncHelper.RunSync(() => this.AuthenticateAsync(scopes));
        }

        /// <summary>
        /// This method is used to retrieve and authorize the client for back-channel communication to the Identity API.
        /// </summary>
        /// <param name="scopes">Contains the scopes that are requested for the client credentials authentication.</param>
        /// <param name="cancellationToken">Contains an optional cancellation token.</param>
        /// <example>
        /// <code>
        ///BackchannelClient client = new BackchannelClient(config)
        ///string scopes = string.Empty;
        ///client.AuthenticateAsync(scopes, cancellationToken);
        /// </code>
        /// </example>
        /// <returns>Returns a value indicating whether the authentication was successful.</returns>
        /// <exception cref="BackchannelClientException">The exception is thrown if an issue occurs during discovery or client authentication.</exception>
        public async Task<bool> AuthenticateAsync(string scopes = "", CancellationToken cancellationToken = default)
        {
            bool authenticationSuccessful;
            string requestScopes = scopes + (!string.IsNullOrWhiteSpace(scopes) ? " " : string.Empty) + string.Join(" ", this.Configuration.Scopes);

            if (this.Configuration.AuthenticationMethod == ClientAuthenticationMethods.Delegation && string.IsNullOrWhiteSpace(this.Configuration.DelegatedAccessToken))
            {
                throw new BackchannelClientException(this.Configuration, Resources.AccessMethodRequiresTokenErrorText);
            }

            // if we're using discovery and need to call it...
            if (this.discovery == null && this.Configuration.UseDiscoveryForEndpoint)
            {
                // get the discovery document.
                this.discovery = await this.RequestDiscoveryAsync(this.Configuration.AuthorityUri, cancellationToken);

                if (this.discovery.IsError)
                {
                    throw new BackchannelClientException(this.Configuration, this.discovery.Error);
                }
            }

            switch (this.Configuration.AuthenticationMethod)
            {
                case ClientAuthenticationMethods.Delegation:
                    this.TokenResponse = await this.RequestDelegationAsync(requestScopes, this.Configuration.DelegatedAccessToken, cancellationToken).ConfigureAwait(false);
                    break;

                case ClientAuthenticationMethods.ClientCredentials:
                    this.TokenResponse = await this.RequestClientCredentialsAsync(requestScopes, cancellationToken).ConfigureAwait(false);
                    break;

                case ClientAuthenticationMethods.ResourceOwnerPassword:
                    this.TokenResponse = await this.RequestResourceOwnerPasswordAsync(this.Configuration.UserId, this.Configuration.Password, requestScopes, cancellationToken).ConfigureAwait(false);
                    break;
            }

            authenticationSuccessful = this.TokenResponse != null && !this.TokenResponse.IsError;

            // an error occurred...
            if (!authenticationSuccessful)
            {
                string errorMessage = this.TokenResponse.Error + Environment.NewLine + this.TokenResponse.ErrorDescription;
                this.LastErrorResponse = new BackchannelErrorResponseModel();
                this.LastErrorResponse.Messages.Add(new BackchannelErrorModel
                {
                    Message = errorMessage,
                    EventDate = DateTime.UtcNow
                });

                // bubble up an error response.
                throw new BackchannelClientException(this.Configuration, errorMessage);
            }

            return authenticationSuccessful;
        }

        /// <summary>
        /// This method is used to easily create a new WebRequest object for the Web API.
        /// </summary>
        /// <param name="relativeUri">Contains the relative Uri path of the web request to make against the Web API.</param>
        /// <param name="method">Contains the HttpMethod request method object.</param>
        /// <param name="noCache">Contains a value indicating whether the URL shall contain a parameter preventing the server from returning cached content.</param>
        /// <param name="credentials">Contains optional credentials</param>
        /// <param name="contentType">Contains optional content type.</param>
        /// <example>
        /// <code>
        ///long projectId = 123;
        ///BackchannelClient client = new BackchannelClient(config)
        ///var request = client.CreateRequest($"/Projects/{projectId}");
        /// </code>
        /// </example>
        /// <returns>Returns a new WebRequest object to execute.</returns>
        public HttpWebRequest CreateRequest(string relativeUri, HttpMethod method = null, bool noCache = true, ICredentials credentials = null, string contentType = "application/json")
        {
            if (string.IsNullOrWhiteSpace(relativeUri))
            {
                throw new ArgumentNullException(nameof(relativeUri));
            }

            if (method == null)
            {
                method = HttpMethod.Get;
            }

            return this.CreateRequest(relativeUri, method.Method, noCache, credentials, contentType);
        }

        /// <summary>
        /// This method is used to easily create a new WebRequest object for the Web API.
        /// </summary>
        /// <param name="relativeUri">Contains the relative Uri path of the web request to make against the Web API.</param>
        /// <param name="method">Contains the request method as a string value.</param>
        /// <param name="noCache">Contains a value indicating whether the URL shall contain a parameter preventing the server from returning cached content.</param>
        /// <param name="credentials">Contains optional credentials</param>
        /// <param name="contentType">Contains optional content type.</param>
        /// <example>
        /// <code>
        ///long projectId = 123;
        ///BackchannelClient client = new BackchannelClient(config)
        ///var request = client.CreateRequest($"/Projects/{projectId}", HttpMethod.Get, true, null, "application/json");
        /// </code>
        /// </example>
        /// <returns>Returns a new HttpWebRequest object to execute.</returns>
        public HttpWebRequest CreateRequest(string relativeUri, string method, bool noCache = true, ICredentials credentials = null, string contentType = "application/json")
        {
            // request /Token, on success, return and store token.
            var request = WebRequest.CreateHttp(new Uri(this.Configuration.ResourceUri, relativeUri));
            request.Method = method;

            if (credentials == null)
            {
                credentials = CredentialCache.DefaultCredentials;
                request.UseDefaultCredentials = true;
            }

            request.Credentials = credentials;
            request.UserAgent = "Talegen Backchannel Client";
            request.Accept = "application/json";
            request.ContentType = contentType;

            // Set a cache policy level for the "http:" and "https" schemes.
            if (noCache)
            {
                request.CachePolicy = new HttpRequestCachePolicy(HttpRequestCacheLevel.NoCacheNoStore);
            }

            if (this.HasAuthenticated)
            {
                request.Headers.Add("Authorization", "Bearer " + this.AccessToken);
            }

            return request;
        }

        /// <summary>
        /// This method is used to execute a web request and return the results of the request as a defined object type.
        /// </summary>
        /// <typeparam name="T">Contains the type of the object that is to be sent with the request.</typeparam>
        /// <typeparam name="TOut">Contains the type of object that is returned from the request.</typeparam>
        /// <param name="request">Contains the HttpWebRequest to execute.</param>
        /// <param name="requestBodyModel">Contains the object to serialize and submit with the request.</param>
        /// <returns>Returns the content of the request response as the specified object.</returns>
        public TOut RequestContent<T, TOut>(HttpWebRequest request, T requestBodyModel)
        {
            string content = this.RequestContent(request, requestBodyModel);

            return !string.IsNullOrWhiteSpace(content) && !this.HasErrors ? JsonConvert.DeserializeObject<TOut>(content) : default(TOut);
        }

        /// <summary>
        /// This method is used to execute a web request and return the results of the request as a defined object type.
        /// </summary>
        /// <typeparam name="T">Contains the type of the object that is to be sent with the request.</typeparam>
        /// <typeparam name="TOut">Contains the type of object that is returned from the request.</typeparam>
        /// <param name="request">Contains the HttpWebRequest to execute.</param>
        /// <param name="requestBodyModel">Contains the object to serialize and submit with the request.</param>
        /// <param name="cancellationToken">Contains an optional cancellation token.</param>
        /// <returns>Returns the content of the request response as the specified object.</returns>
        public async Task<TOut> RequestContentAsync<T, TOut>(HttpWebRequest request, T requestBodyModel, CancellationToken cancellationToken = default)
        {
            string content = await this.RequestContentAsync(request, requestBodyModel, cancellationToken);

            return !string.IsNullOrWhiteSpace(content) && !this.HasErrors ? JsonConvert.DeserializeObject<TOut>(content) : default(TOut);
        }

        /// <summary>
        /// This method is used to execute a web request and return the results of the request as a defined object type.
        /// </summary>
        /// <typeparam name="TOut">Contains the type of object that is returned from the request.</typeparam>
        /// <param name="request">Contains the HttpWebRequest to execute.</param>
        /// <returns>Returns the content of the request response as the specified object.</returns>
        public TOut RequestContent<TOut>(HttpWebRequest request)
        {
            string content = this.RequestContent(request);

            return !string.IsNullOrWhiteSpace(content) && !this.HasErrors ? JsonConvert.DeserializeObject<TOut>(content) : default(TOut);
        }

        /// <summary>
        /// This method is used to execute a web request and return the results of the request as a defined object type.
        /// </summary>
        /// <typeparam name="TOut">Contains the type of object that is returned from the request.</typeparam>
        /// <param name="request">Contains the HttpWebRequest to execute.</param>
        /// <returns>Returns the content of the request response as the specified object.</returns>
        public async Task<TOut> RequestContentAsync<TOut>(HttpWebRequest request)
        {
            string content = await this.RequestContentAsync(request);

            return !string.IsNullOrWhiteSpace(content) && !this.HasErrors ? JsonConvert.DeserializeObject<TOut>(content) : default(TOut);
        }

        /// <summary>
        /// Requests the content stream.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>Returns a byte array of content streamed from the request.</returns>
        public byte[] RequestContentStream(HttpWebRequest request)
        {
            UTF8Encoding utf8 = new UTF8Encoding(true, true);
            string content = this.RequestContent(request);

            return !string.IsNullOrWhiteSpace(content) && !this.HasErrors ? utf8.GetBytes(content) : default(byte[]);
        }

        /// <summary>
        /// Requests the content stream.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>Returns a byte array of content streamed from the request.</returns>
        public async Task<byte[]> RequestContentStreamAsync(HttpWebRequest request)
        {
            UTF8Encoding utf8 = new UTF8Encoding(true, true);
            string content = await this.RequestContentAsync(request);

            return !string.IsNullOrWhiteSpace(content) && !this.HasErrors ? utf8.GetBytes(content) : default(byte[]);
        }

        /// <summary>
        /// Requests the content stream.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="request">The request.</param>
        /// <param name="requestBodyModel">The request body model.</param>
        /// <returns>Returns a byte array of content streamed from the request.</returns>
        public byte[] RequestContentStream<T>(HttpWebRequest request, T requestBodyModel)
        {
            UTF8Encoding utf8 = new UTF8Encoding(true, true);
            string content = this.RequestContent(request, requestBodyModel);

            return !string.IsNullOrWhiteSpace(content) && !this.HasErrors ? utf8.GetBytes(content) : default(byte[]);
        }

        /// <summary>
        /// Requests the content stream.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="request">The request.</param>
        /// <param name="requestBodyModel">The request body model.</param>
        /// <param name="cancellationToken">Contains an optional cancellation token.</param>
        /// <returns>Returns a byte array of content streamed from the request.</returns>
        public async Task<byte[]> RequestContentStreamAsync<T>(HttpWebRequest request, T requestBodyModel, CancellationToken cancellationToken = default)
        {
            UTF8Encoding utf8 = new UTF8Encoding(true, true);
            string content = await this.RequestContentAsync(request, requestBodyModel, cancellationToken);

            return !string.IsNullOrWhiteSpace(content) && !this.HasErrors ? utf8.GetBytes(content) : default;
        }

        /// <summary>
        /// This method is used to execute a web request and return the results of the request as a string.
        /// </summary>
        /// <typeparam name="T">Contains the type of the object that is to be sent with the request.</typeparam>
        /// <param name="request">Contains the HttpWebRequest to execute.</param>
        /// <param name="requestBodyModel">Contains the object to serialize and submit with the request.</param>
        /// <example>
        /// <code>
        ///long targetFolderId = 32;
        ///
        ///BackchannelClient client = new BackchannelClient(config)
        ///var request = client.CreateRequest($"/Backchannel/Users/{userId}", HttpMethod.Put);
        ///client.RequestContent(request, componentIds);
        /// </code>
        /// </example>
        /// <returns>Returns the content of the request response.</returns>
        public string RequestContent<T>(HttpWebRequest request, T requestBodyModel)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (requestBodyModel == null)
            {
                throw new ArgumentNullException(nameof(requestBodyModel));
            }

            // check to ensure we're not trying to post data on a GET or other non-body request.
            if (request.Method != HttpMethod.Post.Method && request.Method != HttpMethod.Put.Method && request.Method != HttpMethod.Delete.Method)
            {
                throw new HttpRequestException(Resources.InvalidRequestTypeErrorText);
            }

            byte[] requestData = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestBodyModel));

            // write data out to the request stream
            using (var postStream = request.GetRequestStream())
            {
                postStream.Write(requestData, 0, requestData.Length);
            }

            return this.RequestContent(request);
        }

        /// <summary>
        /// This method is used to execute a web request and return the results of the request as a string.
        /// </summary>
        /// <typeparam name="T">Contains the type of the object that is to be sent with the request.</typeparam>
        /// <param name="request">Contains the HttpWebRequest to execute.</param>
        /// <param name="requestBodyModel">Contains the object to serialize and submit with the request.</param>
        /// <param name="cancellationToken">Contains an optional cancellation token.</param>
        /// <example>
        /// <code>
        ///long targetFolderId = 32;
        ///
        ///BackchannelClient client = new BackchannelClient(config)
        ///var request = client.CreateRequest($"/Backchannel/Users/{userId}", HttpMethod.Put);
        ///client.RequestContent(request, componentIds);
        /// </code>
        /// </example>
        /// <returns>Returns the content of the request response.</returns>
        public async Task<string> RequestContentAsync<T>(HttpWebRequest request, T requestBodyModel, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (requestBodyModel == null)
            {
                throw new ArgumentNullException(nameof(requestBodyModel));
            }

            // check to ensure we're not trying to post data on a GET or other non-body request.
            if (request.Method != HttpMethod.Post.Method && request.Method != HttpMethod.Put.Method && request.Method != HttpMethod.Delete.Method)
            {
                throw new HttpRequestException(Resources.InvalidRequestTypeErrorText);
            }

            byte[] requestData = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(requestBodyModel));

            // write data out to the request stream
            using (var postStream = await request.GetRequestStreamAsync())
            {
                await postStream.WriteAsync(requestData, 0, requestData.Length, cancellationToken);
            }

            return await this.RequestContentAsync(request);
        }

        /// <summary>
        /// This method is used to execute a web request and return the results of the request as a string.
        /// </summary>
        /// <param name="request">Contains the HttpWebRequest to execute.</param>
        /// <example>
        /// <code>
        ///long attributeId = 321;
        ///
        ///BackchannelClient client = new BackchannelClient(config)
        ///var request = client.CreateRequest($"/Backchannel/Users/{userId}", HttpMethod.Delete);
        ///client.RequestContent(request);
        /// </code>
        /// </example>
        /// <returns>Returns the content of the request response.</returns>
        public string RequestContent(HttpWebRequest request)
        {
            string resultContent = string.Empty;
            this.ResetErrors();

            try
            {
                // execute the request
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    using (var responseStream = response.GetResponseStream())
                    {
                        if (responseStream != null)
                        {
                            resultContent = new StreamReader(responseStream).ReadToEnd();

                            // if the status code was an error and there's content...
                            if ((int)response.StatusCode >= 400 && !string.IsNullOrWhiteSpace(resultContent))
                            {
                                // set the error model
                                this.LastErrorResponse = JsonConvert.DeserializeObject<BackchannelErrorResponseModel>(resultContent);
                            }
                        }
                    }
                }
            }
            catch (WebException webEx)
            {
                this.LastException = webEx;

                if (webEx.Response != null)
                {
                    using (var exceptionResponse = (HttpWebResponse)webEx.Response)
                    {
                        if (exceptionResponse != null)
                        {
                            using (var responseStream = exceptionResponse.GetResponseStream())
                            {
                                if (responseStream != null)
                                {
                                    resultContent = new StreamReader(responseStream).ReadToEnd();

                                    // if the status code was an error and there's content...
                                    if ((int)exceptionResponse.StatusCode >= 400 && !string.IsNullOrWhiteSpace(resultContent))
                                    {
                                        // set the error model
                                        this.LastErrorResponse = JsonConvert.DeserializeObject<BackchannelErrorResponseModel>(resultContent);
                                    }
                                }
                            }
                        }
                    }
                }

                if (this.LastErrorResponse == null)
                {
                    var lastErrorResponse = new BackchannelErrorResponseModel();
                    lastErrorResponse.Messages.Add(new BackchannelErrorModel
                    {
                        Message = webEx.Message,
                        StackTrace = webEx.StackTrace,
                        EventDate = DateTime.UtcNow,
                        ErrorType = ErrorType.Critical,
                        PropertyName = nameof(HttpWebResponse)
                    });

                    this.LastErrorResponse = lastErrorResponse;
                }
            }

            return resultContent;
        }

        /// <summary>
        /// This method is used to execute a web request and return the results of the request as a string.
        /// </summary>
        /// <param name="request">Contains the HttpWebRequest to execute.</param>
        /// <example>
        /// <code>
        ///long attributeId = 321;
        ///
        ///BackchannelClient client = new BackchannelClient(config)
        ///var request = client.CreateRequest($"/Backchannel/Users/{userId}", HttpMethod.Delete);
        ///client.RequestContent(request);
        /// </code>
        /// </example>
        /// <returns>Returns the content of the request response.</returns>
        public async Task<string> RequestContentAsync(HttpWebRequest request)
        {
            string resultContent = string.Empty;
            this.ResetErrors();

            try
            {
                // execute the request
                using (var response = await request.GetResponseAsync() as HttpWebResponse)
                using (var responseStream = response.GetResponseStream())
                {
                    if (responseStream != null)
                    {
                        resultContent = await new StreamReader(responseStream).ReadToEndAsync();

                        // if the status code was an error and there's content...
                        if ((int)response.StatusCode >= 400 && !string.IsNullOrWhiteSpace(resultContent))
                        {
                            // set the error model
                            this.LastErrorResponse = JsonConvert.DeserializeObject<BackchannelErrorResponseModel>(resultContent);
                        }
                    }
                }
            }
            catch (WebException webEx)
            {
                this.LastException = webEx;

                if (webEx.Response != null)
                {
                    using (var exceptionResponse = (HttpWebResponse)webEx.Response)
                    {
                        if (exceptionResponse != null)
                        {
                            using (var responseStream = exceptionResponse.GetResponseStream())
                            {
                                if (responseStream != null)
                                {
                                    resultContent = new StreamReader(responseStream).ReadToEnd();

                                    // if the status code was an error and there's content...
                                    if ((int)exceptionResponse.StatusCode >= 400 && !string.IsNullOrWhiteSpace(resultContent))
                                    {
                                        // set the error model
                                        this.LastErrorResponse = JsonConvert.DeserializeObject<BackchannelErrorResponseModel>(resultContent);
                                    }
                                }
                            }
                        }
                    }
                }

                if (this.LastErrorResponse == null)
                {
                    var lastErrorResponse = new BackchannelErrorResponseModel();
                    lastErrorResponse.Messages.Add(new BackchannelErrorModel
                    {
                        Message = webEx.Message,
                        StackTrace = webEx.StackTrace,
                        EventDate = DateTime.UtcNow,
                        ErrorType = ErrorType.Critical,
                        PropertyName = nameof(HttpWebResponse)
                    });

                    this.LastErrorResponse = lastErrorResponse;
                }
            }

            return resultContent;
        }

        #endregion Public Methods

        #region IDispose Methods

        /// <summary>
        /// This method is called upon disposal of the client class.
        /// </summary>
        /// <example>
        /// <code>
        ///BackchannelClient client = new BackchannelClient(config)
        ///client.Dispose();
        /// </code>
        /// </example>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion IDispose Methods

        #region Protected Methods

        /// <summary>
        /// This method is called upon disposal of the client class.
        /// </summary>
        /// <param name="disposing">Contains a value indicating whether the class is currently being disposed.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposed && disposing)
            {
                // if we're still logged in, be sure to log off.
                if (this.HasAuthenticated)
                {
                    this.ResetSession();
                }

                this.disposed = true;
            }
        }

        /// <summary>
        /// This method is used to clear any previous error objects.
        /// </summary>
        protected void ResetErrors()
        {
            this.LastErrorResponse = null;
            this.LastException = null;
        }

        #endregion Protected Methods

        #region Protected Methods

        /// <summary>
        /// This method is used to contact an authority discovery endpoint for information for interacting with the authority.
        /// </summary>
        /// <param name="authorityUri">Contains the authority URI to contact.</param>
        /// <param name="cancellationToken">Contains a cancellation token.</param>
        /// <returns>Returns a <see cref="DiscoveryDocumentResponse" /> object when successful.</returns>
        protected async Task<DiscoveryDocumentResponse> RequestDiscoveryAsync(Uri authorityUri, CancellationToken cancellationToken)
        {
            DiscoveryDocumentResponse result;

            using (DiscoveryDocumentRequest documentRequest = new DiscoveryDocumentRequest { Address = authorityUri.ToString() })
            using (HttpClient client = new HttpClient())
            {
                result = await client.GetDiscoveryDocumentAsync(documentRequest,
                cancellationToken)
                .ConfigureAwait(false);
            }

            return result;
        }

        /// <summary>
        /// Requests the client credentials.
        /// </summary>
        /// <param name="scopes">The scopes.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Returns a <see cref="TokenResponse" /> object.</returns>
        protected async Task<TokenResponse> RequestClientCredentialsAsync(string scopes, CancellationToken cancellationToken)
        {
            TokenResponse result;

            using (HttpClient client = new HttpClient())
            {
                result = await client.RequestClientCredentialsTokenAsync(new ClientCredentialsTokenRequest
                {
                    Address = this.TokenEndpoint,
                    ClientId = this.Configuration.ClientId,
                    Scope = scopes,
                    ClientSecret = this.Configuration.Secret
                }, cancellationToken)
                    .ConfigureAwait(false);
            }

            return result;
        }

        /// <summary>
        /// This method is used to request a delegation token from the identity server that supports the custom grant type of "delegation".
        /// </summary>
        /// <param name="scopes">The scopes.</param>
        /// <param name="delegatedAccessToken">Contains the delegated access token.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>Returns a new token response from the request.</returns>
        protected async Task<TokenResponse> RequestDelegationAsync(string scopes, string delegatedAccessToken, CancellationToken cancellationToken)
        {
            TokenResponse result;

            using (HttpClient client = new HttpClient())
            {
                result = await client.RequestTokenAsync(new TokenRequest
                {
                    Address = this.TokenEndpoint,
                    GrantType = DelegationGrantTypeName,
                    ClientId = this.Configuration.ClientId, // this would be the ID of the API1 client
                    ClientSecret = this.Configuration.Secret,
                    Parameters =
                    {
                        { "scope", scopes },
                        { "token", delegatedAccessToken }
                    }
                }, cancellationToken)
                    .ConfigureAwait(false);
            }

            return result;
        }

        /// <summary>
        /// Request the resource owner credentials.
        /// </summary>
        /// <param name="userName">Contains the user name.</param>
        /// <param name="password">Contains the user password.</param>
        /// <param name="scopes">Contains the scopes.</param>
        /// <param name="cancellationToken">Contains a cancellation token.</param>
        /// <returns>Returns a <see cref="TokenResponse" /> object.</returns>
        protected async Task<TokenResponse> RequestResourceOwnerPasswordAsync(string userName, string password, string scopes, CancellationToken cancellationToken)
        {
            TokenResponse result;

            using (HttpClient client = new HttpClient())
            {
                var config = new PasswordTokenRequest
                {
                    Address = this.TokenEndpoint,
                    ClientId = this.Configuration.ClientId,
                    Scope = scopes,
                    ClientSecret = this.Configuration.Secret,
                    UserName = userName,
                    Password = password
                };

                result = await client.RequestPasswordTokenAsync(config, cancellationToken)
                    .ConfigureAwait(false);
            }

            return result;
        }

        #endregion Protected Methods

        #region Private Methods

        /// <summary>
        /// This method is used to reset session-related fields.
        /// </summary>
        private void ResetSession()
        {
            this.ResetErrors();
            this.TokenResponse = null;
        }

        #endregion Private Methods
    }
}