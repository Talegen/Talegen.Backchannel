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

    /// <summary>
    /// This class extends the default client exception to include additional configuration detail.
    /// </summary>
    [Serializable]
    public class BackchannelClientException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BackchannelClientException" /> class.
        /// </summary>
        public BackchannelClientException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BackchannelClientException" /> class.
        /// </summary>
        /// <param name="config">Contains the optional inspire client configuration settings.</param>
        /// <param name="message">Contains a message.</param>
        /// <param name="innerException">Contains an optional inner exception.</param>
        public BackchannelClientException(BackchannelConfig config, string message = "", Exception innerException = null)
            : base(message, innerException)
        {
            this.ClientConfiguration = config;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BackchannelClientException" /> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public BackchannelClientException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BackchannelClientException" /> class.
        /// </summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="innerException">
        /// The exception that is the cause of the current exception, or a null reference (Nothing in Visual Basic) if no inner exception is specified.
        /// </param>
        public BackchannelClientException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Gets the inspire client configuration settings.
        /// </summary>
        public BackchannelConfig ClientConfiguration { get; }
    }
}