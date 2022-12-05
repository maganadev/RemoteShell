using CliWrap;
using CliWrap.Buffered;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

#pragma warning disable 8600

namespace RemoteShell
{
    public class RemoteShell
    {
        public const int Port = 8069;
        public const string separator = "|||";
        public static UInt64 uid = 0;
        public static IPEndPoint IncomingEP = new IPEndPoint(0, 0);

        public static UdpClient udpClient = new UdpClient();
        public static CancellationTokenSource tokenSource = new CancellationTokenSource();

        public static void Main()
        {
            //Bind client
            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, Port));

            //Generate uid
            using (RandomNumberGenerator rand = RandomNumberGenerator.Create())
            {
                byte[] UInt64Array = new byte[8];
                rand.GetBytes(UInt64Array);
                uid = BitConverter.ToUInt64(UInt64Array);
                Console.WriteLine("UID: " + uid);
            }

            //Start message server
            Task.Run(() =>
            {
                IncomingUDPServer();
            });

            //Recieve commands
            while (true)
            {
                //Get command
                string command = Console.ReadLine();
                command = command == null ? "" : command;
                string[] segments = command.Split(separator);

                //Get base64 encoded exeName, args, and concurrCount
                string base64ExeName = Convert.ToBase64String(Encoding.UTF8.GetBytes((segments.Length > 0 ? segments[0] : "")));
                string base64Args = Convert.ToBase64String(Encoding.UTF8.GetBytes((segments.Length > 1 ? segments[1] : "")));
                string base64ConcurrCount = Convert.ToBase64String(Encoding.UTF8.GetBytes((segments.Length > 2 ? segments[2] : "")));

                //Construct string to send
                string packetString =
                    "RemoteShell" +
                    separator +
                    uid +
                    separator +
                    base64ExeName +
                    separator +
                    base64Args +
                    separator +
                    base64ConcurrCount;
                byte[] packetBytes = Encoding.UTF8.GetBytes(packetString);

                udpClient.Send(packetBytes, packetBytes.Length, "255.255.255.255", Port);
            }
        }

        public static void IncomingUDPServer()
        {
            while (true)
            {
                try
                {
                    while (true)
                    {
                        //Deal with message
                        string packetString = Encoding.UTF8.GetString(udpClient.Receive(ref IncomingEP));
                        handlePacket(packetString);
                    }
                }
                catch (Exception) {/*Do nothing*/}

                //If we have gotten here, the message server crashed.
                //Wait 1 second and try again.
                Thread.Sleep(1000);
            }
        }

        public static void handlePacket(string packetString)
        {
            //Check for 5 segments
            string[] segments = packetString.Split(separator);
            if (segments.Length != 5)
            {
                return;
            }

            //Check if the first segment is RemoteShell
            if (segments[0] != "RemoteShell")
            {
                return;
            }

            //Bail with print if the message came from us
            bool didCommandComeFromUs = Convert.ToUInt64(segments[1]) == uid;

            //Get exeName
            string exeName = "";
            try
            {
                exeName = Encoding.UTF8.GetString(Convert.FromBase64String(segments[2]));
            }
            catch (Exception) { }

            //Get args
            string args = "";
            try
            {
                args = Encoding.UTF8.GetString(Convert.FromBase64String(segments[3]));
            }
            catch (Exception) { }

            //Get threadsToCreate
            int threadsToCreate = 1;
            try
            {
                string segment4 = Encoding.UTF8.GetString(Convert.FromBase64String(segments[4]));
                threadsToCreate = Convert.ToInt32(segment4);
            }
            catch (Exception) { }

            //Print command plaintext
            Console.WriteLine("RECIEVED COMMAND: " + exeName + " " + args + " " + threadsToCreate);

            //Switch based on command
            switch (exeName)
            {
                case "!stop":
                    tokenSource.Cancel();
                    break;
                case "!start":
                    tokenSource.Cancel();
                    tokenSource = new CancellationTokenSource();
                    break;
                default:
                    //Create threads
                    if (!didCommandComeFromUs)
                    {
                        for (int i = 0; i < threadsToCreate; i++)
                        {
                            Cli.Wrap(exeName).WithArguments(args).ExecuteBufferedAsync(tokenSource.Token);
                        }
                    }
                    else
                    {
                        Console.WriteLine("Message recieved from ourselves, ignoring");
                        return;
                    }
                    break;
            }
        }
    }
}