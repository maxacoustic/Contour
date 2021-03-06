﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Common.Logging;

using Contour.Configuration;
using Contour.Filters;
using Contour.Helpers;

namespace Contour.Sending
{
    /// <summary>
    /// Отправитель, который не знает о транспортном уровне.
    /// </summary>
    internal abstract class AbstractSender : ISender
    {
        private static readonly ILog Logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Фильтры обработки сообщений.
        /// </summary>
        private readonly IList<IMessageExchangeFilter> filters;

        /// <summary>
        /// Конечная точка, от имени которой работает отправитель.
        /// </summary>
        private readonly IEndpoint endpoint;

        /// <summary>
        /// Последняя точка в пути сообщения.
        /// </summary>
        private readonly string breadCrumbsTail;

        private readonly IDictionary<Type, IMessageExchangeFilterDecorator> filterDecorators;
        // TODO: refactor, don't copy filters

        /// <summary>
        /// Initializes a new instance of the <see cref="AbstractSender"/> class. 
        /// </summary>
        /// <param name="endpoint">
        /// Sender's endpoint
        /// </param>
        /// <param name="configuration">
        /// Sender's configuration
        /// </param>
        /// <param name="filters">
        /// A list of message handling filters
        /// </param>
        protected AbstractSender(IEndpoint endpoint, ISenderConfiguration configuration, IEnumerable<IMessageExchangeFilter> filters)
        {
            this.endpoint = endpoint;
            this.breadCrumbsTail = ";" + endpoint.Address;

            this.filters = new SendingExchangeFilter(this.InternalSend)
                .ToEnumerable()
                .Union(filters)
                .ToList();

            this.Configuration = configuration;
        }
        protected AbstractSender(IEndpoint endpoint, ISenderConfiguration configuration, IEnumerable<IMessageExchangeFilter> filters, IDictionary<Type, IMessageExchangeFilterDecorator> filterDecorators)
            : this(endpoint, configuration, filters)
        {
            this.filterDecorators = filterDecorators;
        }

        /// <summary>
        /// Конфигурация отправителя.
        /// </summary>
        public ISenderConfiguration Configuration { get; private set; }

        /// <summary>
        /// Есть ли сбои в работе отправителя.
        /// </summary>
        public abstract bool IsHealthy { get; }

        /// <summary>
        /// Проверяет возможность создать маршрут для метки сообщения.
        /// </summary>
        /// <param name="label">Метка сообщения, для которой нужно создать маршрут.</param>
        /// <returns><c>true</c> - если можно создать маршрут.</returns>
        public virtual bool CanRoute(MessageLabel label)
        {
            return label.IsAlias ? label.Name.Equals(this.Configuration.Alias) : label.Equals(this.Configuration.Label);
        }

        /// <summary>
        /// Освобождает ресурсы.
        /// </summary>
        public abstract void Dispose();

        /// <summary>
        /// Sends message using request-reply pattern. 
        /// <see cref="Headers.CorrelationId"/> header from <see cref="headers"/> parameter is used to correlate the request with the reply, 
        /// new one is generated if none is supplied.
        /// </summary>
        /// <param name="payload">Message payload.</param>
        /// <param name="headers">Message headers.</param>
        /// <typeparam name="T">Type of response.</typeparam>
        /// <returns>Request processing task.</returns>
        public Task<T> Request<T>(object payload, IDictionary<string, object> headers) where T : class
        {
            var message = new Message(this.Configuration.Label, headers, payload);

            var exchange = new MessageExchange(message, typeof(T));
            var invoker = this.GetMessageExchangeFilterInvoker();

            return invoker.Process(exchange)
                .ContinueWith(
                    t =>
                        {
                            t.Result.ThrowIfFailed();
                            return (T)t.Result.In.Payload;
                        });
        }

        /// <summary>
        /// Sends message using request-reply pattern.
        /// Copies all allowed message headers and generates new <see cref="Headers.CorrelationId"/> header. 
        /// <seealso ref="https://github.com/SDVentures/Contour#contour-message-headers"/>.
        /// </summary>
        /// <param name="payload">Message payload.</param>
        /// <param name="options">Request options.</param>
        /// <typeparam name="T">Type of response.</typeparam>
        /// <returns>Request processing task.</returns>
        public Task<T> Request<T>(object payload, RequestOptions options) where T : class
        {
            var headers = this.ApplyOptions(options);
            headers[Headers.CorrelationId] = Guid.NewGuid().ToString("n");

            Logger.Trace(m => m("Message original Id [{0}], correlation Id [{1}].", Headers.GetString(headers, Headers.OriginalMessageId), Headers.GetString(headers, Headers.CorrelationId)));

            return this.Request<T>(payload, headers);
        }

        /// <summary>
        /// Отправляет одностороннее сообщение.
        /// </summary>
        /// <param name="payload">Тело сообщения.</param>
        /// <param name="headers">Заголовки сообщения.</param>
        /// <returns>Задача выполнения отправки сообщения.</returns>
        [Obsolete("Необходимо использовать метод Send с указанием метки сообщения.")]
        public Task Send(object payload, IDictionary<string, object> headers)
        {
            var message = new Message(this.Configuration.Label, headers, payload);

            return this.ProcessFilter(message);
        }

        /// <summary>
        /// Отправляет одностороннее сообщение.
        /// </summary>
        /// <param name="payload">Тело сообщения.</param>
        /// <param name="options">Заголовки сообщения.</param>
        /// <returns>Задача выполнения отправки сообщения.</returns>
        [Obsolete("Необходимо использовать метод Send с указанием метки сообщения.")]
        public Task Send(object payload, PublishingOptions options)
        {
            return this.Send(payload, this.ApplyOptions(options));
        }

        /// <summary>
        /// Sends message using request-reply pattern.
        /// Copies all allowed message headers. And generates new <see cref="Headers.CorrelationId"/> header. 
        /// <seealso ref="https://github.com/SDVentures/Contour#contour-message-headers"/>.
        /// </summary>
        /// <param name="label">Message label.</param>
        /// <param name="payload">Message payload.</param>
        /// <param name="options">Request options.</param>
        /// <typeparam name="T">Type of response.</typeparam>
        /// <returns>Request processing task.</returns>
        public Task<T> Request<T>(MessageLabel label, object payload, RequestOptions options) where T : class
        {
            var headers = this.ApplyOptions(options);
            headers[Headers.CorrelationId] = Guid.NewGuid().ToString("n");
            
            return this.Request<T>(label, payload, headers);
        }

        /// <summary>
        /// Sends message using request-reply pattern. 
        /// <see cref="Headers.CorrelationId"/> header from <see cref="headers"/> parameter is used to correlate the request with the reply, 
        /// new one is generated if none is supplied.
        /// </summary>
        /// <param name="label">Message label.</param>
        /// <param name="payload">Message payload.</param>
        /// <param name="headers">Message headers.</param>
        /// <typeparam name="T">Type of response.</typeparam>
        /// <returns>Request processing task.</returns>
        public Task<T> Request<T>(MessageLabel label, object payload, IDictionary<string, object> headers) where T : class
        {
            if (!headers.ContainsKey(Headers.CorrelationId))
            {
                headers[Headers.CorrelationId] = Guid.NewGuid().ToString("n");
            }

            Logger.Trace($"Requesting: label=[{label}], correlationId=[{headers[Headers.CorrelationId]}]");
            var message = new Message(this.Configuration.Label.Equals(MessageLabel.Any) ? label : this.Configuration.Label, headers, payload);

            var exchange = new MessageExchange(message, typeof(T));
            var invoker = this.GetMessageExchangeFilterInvoker();

            return invoker.Process(exchange)
                .ContinueWith(
                    t =>
                    {
                        t.Result.ThrowIfFailed();
                        return (T)t.Result.In.Payload;
                    });
        }

        /// <summary>
        /// Отправляет одностороннее сообщение.
        /// </summary>
        /// <param name="label">Метка отправляемого сообщения.</param>
        /// <param name="payload">Тело сообщения.</param>
        /// <param name="headers">Заголовки сообщения.</param>
        /// <returns>Задача выполнения отправки сообщения.</returns>
        public Task Send(MessageLabel label, object payload, IDictionary<string, object> headers)
        {
            var message = new Message(this.Configuration.Label.Equals(MessageLabel.Any) ? label : this.Configuration.Label, headers, payload);

            return this.ProcessFilter(message);
        }

        /// <summary>
        /// Отправляет одностороннее сообщение.
        /// </summary>
        /// <param name="label">Метка отправляемого сообщения.</param>
        /// <param name="payload">Тело сообщения.</param>
        /// <param name="options">Заголовки сообщения.</param>
        /// <returns>Задача выполнения отправки сообщения.</returns>
        public Task Send(MessageLabel label, object payload, PublishingOptions options)
        {
            return this.Send(label, payload, this.ApplyOptions(options));
        }

        /// <summary>
        /// Запускает отправитель.
        /// </summary>
        public abstract void Start();

        /// <summary>
        /// Останавливает отправитель.
        /// </summary>
        public abstract void Stop();

        /// <summary>
        /// Фильтр обработки сообщения, который отсылает сообщение.
        /// </summary>
        /// <param name="exchange">Отсылаемое сообщение.</param>
        /// <returns>Задача выполнения фильтра.</returns>
        protected abstract Task<MessageExchange> InternalSend(MessageExchange exchange);

        private MessageExchangeFilterInvoker GetMessageExchangeFilterInvoker()
        {
            return this.filterDecorators != null ? new MessageExchangeFilterInvoker(this.filters, this.filterDecorators) : new MessageExchangeFilterInvoker(this.filters);
        }
        /// <summary>
        /// Обрабатывает сообщение с помощью зарегистрированных фильтров.
        /// </summary>
        /// <param name="message">Обрабатываемое сообщение.</param>
        /// <returns>Задача обработки сообщения с помощью фильтров.</returns>
        private Task ProcessFilter(IMessage message)
        {
            var exchange = new MessageExchange(message, null);
            var invoker = this.GetMessageExchangeFilterInvoker();

            return invoker.Process(exchange);
        }

        /// <summary>
        /// Конвертирует настройки публикации сообщения в заголовки сообщения.
        /// </summary>
        /// <param name="options">Настройки публикации сообщения.</param>
        /// <returns>Заголовки сообщения.</returns>
        private IDictionary<string, object> ApplyOptions(PublishingOptions options)
        {
            var storage = this.Configuration.Options.GetIncomingMessageHeaderStorage().Value;

            var incomingHeaders = storage.Load() ?? new Dictionary<string, object>();
            var outgoingHeaders = new Dictionary<string, object>(incomingHeaders);

            Headers.ApplyBreadcrumbs(outgoingHeaders, this.endpoint.Address);
            Headers.ApplyOriginalMessageId(outgoingHeaders);

            var defaultOptionsMap = new Dictionary<string, Func<Maybe>>
                                        {
                                            { Headers.Persist, () => this.Configuration.Options.IsPersistently() },
                                            { Headers.Ttl, () => this.Configuration.Options.GetTtl() },
                                            { Headers.Delay, () => this.Configuration.Options.GetDelay() }
                                        };

            var publishingOptionsDict = options.GetAll();

            var keys = defaultOptionsMap.Keys.Union(publishingOptionsDict.Keys).Distinct();

            foreach (var key in keys)
            {
                var maybeValue = publishingOptionsDict.ContainsKey(key)
                                     ? publishingOptionsDict[key]
                                     : defaultOptionsMap[key].Invoke();

                if (maybeValue.HasValue)
                {
                    outgoingHeaders[key] = maybeValue.Value;
                }
            }

            return outgoingHeaders;
        }

        /// <summary>
        /// Конвертирует настройки публикации сообщения в заголовки сообщения.
        /// </summary>
        /// <param name="requestOptions">Настройки публикации сообщения.</param>
        /// <returns>Заголовки сообщения.</returns>
        private IDictionary<string, object> ApplyOptions(RequestOptions requestOptions)
        {
            IDictionary<string, object> headers = this.ApplyOptions(requestOptions as PublishingOptions);

            Maybe<TimeSpan?> timeout = BusOptions.Pick(requestOptions.Timeout, this.Configuration.Options.GetRequestTimeout());
            if (timeout != null && timeout.HasValue)
            {
                headers[Headers.Timeout] = timeout.Value;
            }

            return headers;
        }
    }
}
