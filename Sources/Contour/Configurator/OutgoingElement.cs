﻿namespace Contour.Configurator
{
    using System;
    using System.Configuration;

    /// <summary>
    /// The outgoing element.
    /// </summary>
    internal class OutgoingElement : MessageElement
    {
        /// <summary>
        /// Gets the callback endpoint.
        /// </summary>
        [ConfigurationProperty("callbackEndpoint")]
        public CallbackEndpointElement CallbackEndpoint
        {
            get
            {
                return (CallbackEndpointElement)(base["callbackEndpoint"]);
            }
        }

        /// <summary>
        /// Gets a value indicating whether confirm.
        /// </summary>
        [ConfigurationProperty("confirm")]
        public bool Confirm
        {
            get
            {
                return (bool)(base["confirm"]);
            }
        }

        /// <summary>
        /// Gets a value indicating whether persist.
        /// </summary>
        [ConfigurationProperty("persist")]
        public bool Persist
        {
            get
            {
                return (bool)base["persist"];
            }
        }

        /// <summary>
        /// Gets the timeout.
        /// </summary>
        [ConfigurationProperty("timeout", DefaultValue = "00:00:30")]
        public TimeSpan? Timeout
        {
            get
            {
                return (TimeSpan?)base["timeout"];
            }
        }

        /// <summary>
        /// Gets the ttl.
        /// </summary>
        [ConfigurationProperty("ttl")]
        public TimeSpan? Ttl
        {
            get
            {
                return (TimeSpan?)base["ttl"];
            }
        }

        /// <summary>
        /// Gets the delayed.
        /// </summary>
        [ConfigurationProperty("delayed", DefaultValue = false)]
        public bool Delayed
        {
            get
            {
                return (bool)base["delayed"];
            }
        }

        /// <summary>
        /// Gets the delay.
        /// </summary>
        [ConfigurationProperty("delay")]
        public TimeSpan? Delay
        {
            get
            {
                return (TimeSpan?)base["delay"];
            }
        }

        /// <summary>
        /// Gets the connection string
        /// </summary>
        [ConfigurationProperty("connectionString", IsRequired = false)]
        public string ConnectionString
        {
            get { return (string)base["connectionString"]; }
        }

        /// <summary>
        /// Gets a flag which specifies if an existing connection should be reused
        /// </summary>
        [ConfigurationProperty("reuseConnection", IsRequired = false)]
        public bool? ReuseConnection
        {
            get { return (bool?)base["reuseConnection"]; }
        }
    }
}
