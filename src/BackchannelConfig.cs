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
    using System.Collections.Generic;

    /// <summary>
    /// Contains an enumerated list of SDK client authentication methods.
    /// </summary>
    public enum ClientAuthenticationMethods
    {
        /// <summary>
        /// The client shall authenticate with a client secret.
        /// </summary>
        ClientCredentials,

        /// <summary>
        /// The client shall authenticate with the resource owner using a user id and password.
        /// </summary>
        /// <remarks>The spec recommends using the resource owner password grant only for "trusted" (or legacy) applications.</remarks>
        ResourceOwnerPassword,

        /// <summary>
        /// This method shall allow a client token to be exchanged for another from the authority for client delegation with another API.
        /// </summary>
        Delegation
    }

    /// <summary>
    /// This class provides the minimum configuration settings for a backchannel client.
    /// </summary>
    public class BackchannelConfig
    {
        /// <summary>
        /// Gets or sets the client authentication method used.
        /// </summary>
        public ClientAuthenticationMethods AuthenticationMethod { get; set; } = ClientAuthenticationMethods.ClientCredentials;

        /// <summary>
        /// Gets or sets the client identifier.
        /// </summary>
        /// <value>The client identifier.</value>
        public string ClientId { get; set; }

        /// <summary>
        /// Gets or sets the authority URI.
        /// </summary>
        /// <value>The authority URI.</value>
        public Uri AuthorityUri { get; set; }

        /// <summary>
        /// Gets or sets the resource URI.
        /// </summary>
        /// <value>The resource URI.</value>
        public Uri ResourceUri { get; set; }

        /// <summary>
        /// Gets or sets the secret.
        /// </summary>
        /// <value>The secret.</value>
        public string Secret { get; set; }

        /// <summary>
        /// Gets or sets the list of scopes requested for the client.
        /// </summary>
        /// <value>The scopes.</value>
        public List<string> Scopes { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the delegated access token.
        /// </summary>
        /// <value>The delegated access token.</value>
        public string DelegatedAccessToken { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether [use discovery for endpoint].
        /// </summary>
        /// <value><c>true</c> if [use discovery for endpoint]; otherwise, <c>false</c>.</value>
        public bool UseDiscoveryForEndpoint { get; set; } = true;

        /// <summary>
        /// Gets or sets the user name used for password client authentication.
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// Gets or sets the password used for password client authentication.
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// Gets or sets the user agent.
        /// </summary>
        /// <value>The user agent.</value>
        public string UserAgent { get; set; } = "Talegen Backchannel Client";
    }
}