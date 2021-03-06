﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Xml.Linq;

using Contour.Caching;
using Contour.Serialization;

namespace Contour.Configurator
{
    internal class CacheConfigProvider
    {
        private const string ServiceBusSectionName = "serviceBus/endpoints";

        private const string CachingElementName = "caching";

        private const string DefaultCacheName = "memory-cache";

        private const string EnabledPropertyName = "enabled";

        private const string ProviderPropertyName = "provider";

        private const string TtlPropertyName = "ttl";

        private static readonly ICache DefaultCache = new MemoryCache(new Sha256Hasher(new JsonNetPayloadConverter()));

        private readonly IDependencyResolver dependencyResolver;

        private readonly EndpointsSection endpointsConfig;

        public CacheConfigProvider(IDependencyResolver dependencyResolver)
            :this(dependencyResolver, (EndpointsSection)ConfigurationManager.GetSection(ServiceBusSectionName))
        {
            this.dependencyResolver = dependencyResolver;
        }

        public CacheConfigProvider(IDependencyResolver dependencyResolver, EndpointsSection endpointsConfig)
        {
            this.dependencyResolver = dependencyResolver;
            this.endpointsConfig = endpointsConfig;
        }

        public IEnumerable<string> EndpointNames => this.endpointsConfig.Endpoints.OfType<EndpointElement>().Select(e => e.Name);

        public IDictionary<string, CacheConfiguration> GetOutgoingCacheConfig(string endpointName)
        {
            var endpoint = this.endpointsConfig.Endpoints[endpointName];
            return this.GetConfigFromMessageElements(endpoint, endpoint.Outgoing, o => o.Label);
        }

        public IDictionary<string, CacheConfiguration> GetIncomingCacheConfig(string endpointName)
        {
            var endpoint = this.endpointsConfig.Endpoints[endpointName];
            return this.GetConfigFromMessageElements(endpoint, endpoint.Incoming, o => ((IncomingElement)o).Label);
        }

        private IDictionary<string, CacheConfiguration> GetConfigFromMessageElements(EndpointElement endpoint, IEnumerable configElementsCollection, Func<MessageElement, string> keySelector)
        {
            var defaultConfig = this.GetConfiguration(endpoint.ExtensionConfigurations, this.GetDefault());

            return configElementsCollection.Cast<MessageElement>().ToDictionary(keySelector, o => this.GetConfiguration(o.ExtensionConfigurations, defaultConfig));
        }

        private CacheConfiguration GetConfiguration(IDictionary<string, XNode> extensionConfigurations, CacheConfiguration defaultConfig)
        {
            if (!extensionConfigurations.ContainsKey(CachingElementName))
            {
                return defaultConfig;
            }

            var xmlConfig = extensionConfigurations[CachingElementName] as XElement;

            if (xmlConfig == null)
            {
                return defaultConfig;
            }

            var attributes = xmlConfig.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value);

            var config = this.GetConfiguration(attributes);

            return new CacheConfiguration(config.Enabled ?? defaultConfig.Enabled ?? false, config.Ttl ?? defaultConfig.Ttl, config.Cache ?? defaultConfig.Cache);
        }

        private CacheConfiguration GetConfiguration(IDictionary<string, string> properties)
        {
            bool? cacheEnabled = null;
            string cacheName = null;
            TimeSpan? ttl = null;

            if (properties?.Count > 0)
            {
                if (properties.ContainsKey(EnabledPropertyName))
                {
                    cacheEnabled = ParseHelper.GetParsedValue<bool>(properties, EnabledPropertyName, bool.TryParse);
                }

                cacheName = properties.ContainsKey(ProviderPropertyName) ? properties[ProviderPropertyName] : null;
                ttl = ParseHelper.GetParsedValue<TimeSpan?>(properties, TtlPropertyName, ParseHelper.TryParseNullableTimeSpan);
            }

            return new CacheConfiguration(cacheEnabled, ttl, this.GetCache(string.IsNullOrEmpty(cacheName) ? DefaultCacheName : cacheName));
        }

        private CacheConfiguration GetDefault()
        {
            return new CacheConfiguration(false, null, this.GetCache(DefaultCacheName));
        }

        private ICache GetCache(string name)
        {
            if (name == DefaultCacheName)
            {
                return DefaultCache;
            }

            return (ICache)this.dependencyResolver.Resolve(name, typeof(ICache));
        }
    }
}