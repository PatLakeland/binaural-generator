﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;

namespace NetworkLayer.Protocol
{
    public class ServerProtocol : IDisposable
    {
        IServerConnectionInterface connectionInterface = null;
        Thread sendingWorker = null;
        Thread receivingWorker = null;

        public delegate void ClientConnectionHandler(object sender, ClientInfoEventArgs e);
        public delegate void SettingsReceiveHandler(object sender, SettingsDataEventArgs e);
        public delegate void VoiceWindowReceiveHandler(object sender, VoiceWindowDataEventArgs e);
        public delegate void ChatMessageReceiveHandler(object sender, ClientChatMessageEventArgs e);

        ManualResetEvent sendingThreadStopped = new ManualResetEvent(false);
        ManualResetEvent receivingThreadStopped = new ManualResetEvent(false);

        Queue<Packet> sendingQueue = new Queue<Packet>();
        Queue<Packet> receivedQueue = new Queue<Packet>();

        bool sendingTerminate = false;
        bool receivingTerminate = false;

        string serverName = null;

        public ServerProtocol(string serverName)
        {
            this.serverName = serverName;
        }

        private void SendingWorker()
        {
            while (true)
            {
                if (connectionInterface == null ||
                    !connectionInterface.IsListening() ||
                    !connectionInterface.IsClientConnected() ||
                    sendingTerminate)
                    break;

                if (sendingQueue.Count == 0)
                    continue;

                Packet packetToSend = sendingQueue.Dequeue();
                connectionInterface.Send(packetToSend.SerializedData);
            }

            sendingTerminate = false;
            sendingThreadStopped.Set();
        }

        private void ReceivingWorker()
        {
            List<byte> receivedBuffer = new List<byte>(1024);
            while (true)
            {
                if (connectionInterface == null ||
                    !connectionInterface.IsListening() ||
                    !connectionInterface.IsClientConnected() ||
                    receivingTerminate)
                    break;

                byte[] temporalBuffer = new byte[1024];
                int receivedCount = connectionInterface.Receive(temporalBuffer, 100);
                receivedBuffer.AddRange(temporalBuffer.Take(receivedCount));

                while (receivedBuffer.Count > 0)
                {
                    byte[] protocolHeader = receivedBuffer.Take(sizeof(PacketType) + sizeof(int)).ToArray();
                    PacketType type = (PacketType)protocolHeader[0];
                    int packetDataSize = BitConverter.ToInt32(protocolHeader, 1);

                    if (receivedBuffer.Count < 1 + sizeof(int) + packetDataSize)
                        break;

                    byte[] packetData = receivedBuffer.Skip(sizeof(PacketType) + sizeof(int)).Take(packetDataSize).ToArray();
                    receivedBuffer.RemoveRange(0, 1 + sizeof(int) + packetDataSize);
                    MemoryStream m = new MemoryStream(packetData);
                    BinaryFormatter b = new BinaryFormatter();

                    switch (type)
                    {
                        case PacketType.ChatMessage:
                            {
                                ClientChatMessageEventArgs args = null;
                                args = (ClientChatMessageEventArgs)b.Deserialize(m);
                                ChatMessageReceive(this, args);
                                break;
                            }
                        case PacketType.ProtocolInfoMessage:
                            break;
                        case PacketType.SettingsMessage:
                            {
                                SettingsDataEventArgs args = null;
                                args = (SettingsDataEventArgs)b.Deserialize(m);
                                SettingsReceive(this, args);
                                break;
                            }
                        case PacketType.VoiceMessage:
                            {
                                VoiceWindowDataEventArgs args = null;
                                args = (VoiceWindowDataEventArgs)b.Deserialize(m);
                                VoiceWindowReceive(this, args);
                                break;
                            }
                        case PacketType.Unknown:
                        default:
                            throw new Exception("Unknown protocol message");
                    }
                }
            }

            receivingTerminate = false;
            receivingThreadStopped.Set();
        }

        public bool Bind(string host)
        {
            connectionInterface = new InternetServerConnectionInterface();
            if (connectionInterface == null)
                return false;

            connectionInterface.ClientConnected += ClientConnectedEvent;
            bool result = connectionInterface.StartListening(host, ProtocolShared.protocolPort);

            return result;
        }

        public void Stop()
        {
            if (sendingWorker != null)
            {
                sendingTerminate = true;
                sendingThreadStopped.WaitOne();
                sendingWorker.Abort();
                sendingWorker = null;
            }

            if (receivingWorker != null)
            {
                receivingTerminate = true;
                receivingThreadStopped.WaitOne();
                receivingWorker.Abort();
                receivingWorker = null;
            }

            if (connectionInterface != null)
            {
                lock (connectionInterface)
                {
                    connectionInterface.Shutdown();
                    connectionInterface = null;
                }
            }
        }

        public bool SendSensorsData(double temperatureValue, double skinResistanceValue, double motionValue, double pulseValue)
        {
            MemoryStream m = new MemoryStream();
            BinaryFormatter b = new BinaryFormatter();

            SensorsDataEventArgs data = new SensorsDataEventArgs {
                motionValue = motionValue,
                skinResistanceValue = skinResistanceValue,
                pulseValue = pulseValue,
                temperatureValue = temperatureValue
            };

            b.Serialize(m, data);
            return SendPacket(PacketType.SensorsMessage, m.GetBuffer());
        }

        public bool SendVoiceWindow(byte[] voiceData)
        {
            if (voiceData == null)
                return false;

            MemoryStream m = new MemoryStream();
            BinaryFormatter b = new BinaryFormatter();
            VoiceWindowDataEventArgs data = new VoiceWindowDataEventArgs { data = voiceData };
            b.Serialize(m, data);
            return SendPacket(PacketType.SensorsMessage, m.GetBuffer());
        }

        public bool SendChatMessage(string message)
        {
            if (message == null)
                return false;

            MemoryStream m = new MemoryStream();
            BinaryFormatter b = new BinaryFormatter();
            ClientChatMessageEventArgs msg = new ClientChatMessageEventArgs();
            msg.message = message;
            b.Serialize(m, msg);
            return SendPacket(PacketType.SensorsMessage, m.GetBuffer());
        }

        public void Dispose()
        {
            Stop();
        }

        private bool SendPacket(PacketType type, byte[] data)
        {
            if (!connectionInterface.IsListening() ||
                !connectionInterface.IsClientConnected() ||
                type == PacketType.Unknown ||
                data.Length == 0)
                return false;

            Packet packetToSend = new Packet(type, data);
            sendingQueue.Enqueue(packetToSend);
            return true;
        }

        private void ClientConnectedEvent(object sender, EventArgs e)
        {
            ProtocolInfo protocolInfo = new ProtocolInfo();
            BinaryFormatter formatter = new BinaryFormatter();
            MemoryStream stream = new MemoryStream();
            formatter.Serialize(stream, protocolInfo);

            SendPacket(PacketType.ProtocolInfoMessage, stream.GetBuffer());

            lock (connectionInterface)
            {
                byte[] buffer = new byte[1024];
                int count = connectionInterface.Receive(buffer, 5000);
                if (count > 0)
                {
                    //check info here
                    if (buffer[0] != (byte)PacketType.ClientInfoMessage)
                        return;

                    int packetSize = BitConverter.ToInt32(buffer, 1);
                    if (packetSize <= 0)
                        return;

                    ClientInfoEventArgs info = new ClientInfoEventArgs();
                    info.clientName = Encoding.UTF8.GetString(buffer, 5, packetSize);
                    ClientConnected(this, info);

                    ServerInfoEventArgs serverInfo = new ServerInfoEventArgs { serverName = this.serverName };
                    stream = new MemoryStream();
                    formatter.Serialize(stream, serverInfo);
                    SendPacket(PacketType.ServerInfoMessage, stream.GetBuffer());

                    // everything is ok, start working
                    sendingThreadStopped.Reset();
                    sendingWorker = new Thread(SendingWorker);
                    sendingWorker.Start();

                    receivingThreadStopped.Reset();
                    receivingWorker = new Thread(ReceivingWorker);
                    receivingWorker.Start();
                }
            }
        }

        public event ClientConnectionHandler ClientConnected = delegate
        { };
        public event SettingsReceiveHandler SettingsReceive = delegate
        { };
        public event VoiceWindowReceiveHandler VoiceWindowReceive = delegate
        { };
        public event ChatMessageReceiveHandler ChatMessageReceive = delegate
        { };
    }
}
