using System;
using System.Collections.Generic;

using Contour.Configuration;
using Contour.Helpers;
using Contour.Sending;
using Contour.Transport.RabbitMQ.Internal;
using Contour.Transport.RabbitMQ.Topology;

namespace Contour.Transport.RabbitMQ
{
    /// <summary>
    /// ������ ���������� �������������.
    /// </summary>
    public static class BusConfigurationEx
    {
        /// <summary>
        /// ���������� ����, ������� �������� ��������� <c>Fault</c> ��������.
        /// </summary>
        private static readonly int FaultMessageTtlDays = 21;

        /// <summary>
        /// ������������� ��������� <c>Quality of service (QoS)</c> �� ��������� ��� ���� �����������.
        /// </summary>
        /// <param name="busConfigurator">������������ ���� ���������.</param>
        /// <param name="prefetchCount">���������� ��������� ���������� �� ���� �� ���� ���������, �.�. ������ ������ ������.</param>
        /// <param name="prefetchSize">���������� ���������, ������� ������ ���������� ����������, ������ ��� ������� ����� ������ ������.</param>
        /// <returns>������������ ���������� ���� ���������.</returns>
        public static IBusConfigurator SetDefaultQoS(this IBusConfigurator busConfigurator, ushort prefetchCount, uint prefetchSize = 0)
        {
            // TODO: beautify
            ((RabbitReceiverOptions)((BusConfiguration)busConfigurator).ReceiverDefaults).QoS = new QoSParams(prefetchCount, prefetchSize);

            return busConfigurator;
        }

        /// <summary>
        /// �������� ������������� ������� <c>RabbitMQ</c>.
        /// </summary>
        /// <param name="busConfigurator">������������ ���� ���������.</param>
        /// <returns>������������ ���� ��������� � ���������� ���������� �������.</returns>
        public static IBusConfigurator UseRabbitMq(this IBusConfigurator busConfigurator)
        {
            var c = (BusConfiguration)busConfigurator;
            if (c.BusFactoryFunc != null)
            {
                return busConfigurator;
            }

            var blockedHeaders = new List<string>
                                     {
                                         Headers.Expires,
                                         Headers.MessageLabel,
                                         Headers.Persist,
                                         Headers.QueueMessageTtl,
                                         Headers.ReplyRoute,
                                         Headers.Timeout,
                                         Headers.Ttl
                                     };
            var messageHeaderStorage = new Maybe<IIncomingMessageHeaderStorage>(new MessageHeaderStorage(blockedHeaders));

            c.BuildBusUsing(bc => new RabbitBus(c));

            c.SenderDefaults = new SenderOptions
                                   {
                                       ConfirmationIsRequired = false, 
                                       Persistently = false, 
                                       RequestTimeout = default(TimeSpan?), 
                                       Ttl = default(TimeSpan?), 
                                       RouteResolverBuilder = RabbitBusDefaults.RouteResolverBuilder,
                                       IncomingMessageHeaderStorage = messageHeaderStorage
                                   };

            c.ReceiverDefaults = new RabbitReceiverOptions
                                     {
                                         AcceptIsRequired = false, 
                                         ParallelismLevel = 1, 
                                         EndpointBuilder = RabbitBusDefaults.SubscriptionEndpointBuilder,
                                         QoS = new QoSParams(50, 0),
                                         IncomingMessageHeaderStorage = messageHeaderStorage
                                     };

            c.UseMessageLabelHandler(new DefaultRabbitMessageLabelHandler());

            // TODO: extract, combine routing and handler definition
            Func<IRouteResolverBuilder, IRouteResolver> faultRouteResolverBuilder = b =>
                {
                    string name = b.Endpoint.Address + ".Fault";
                    Exchange e = b.Topology.Declare(
                        Exchange.Named(name).Durable.Topic);
                    Queue q = b.Topology.Declare(Queue.Named(name).Durable.WithTtl(TimeSpan.FromDays(FaultMessageTtlDays)));
                    b.Topology.Bind(e, q);
                    return e;
                };

            c.Route("document.Contour.unhandled").Persistently().ConfiguredWith(faultRouteResolverBuilder);
            c.Route("document.Contour.failed").Persistently().ConfiguredWith(faultRouteResolverBuilder);

            c.OnUnhandled(
                d =>
                    {
                        d.Forward("document.Contour.unhandled", d.BuildFaultMessage());
                        d.Accept();
                    });
            c.OnFailed(
                d =>
                    {
                        d.Forward("document.Contour.failed", d.BuildFaultMessage());
                        d.Accept();
                    });

            return busConfigurator;
        }
    }
}