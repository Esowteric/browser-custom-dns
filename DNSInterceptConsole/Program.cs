/*
 * Created by SharpDevelop.
 * User: Eric Twose.
 * 
 * Sorry, my knowledge of C# is very limited. I could really do with some help
 * from someone who knows DllInject, Marshal.AllocHGlobal(), DNS and getaddrinfo(),
 * who can tidy up, and fix bugs, memory leaks and Firefox crashes.
 * 
 * Requirements: Windows XP SP2 or later; .NET framework 3.5 runtime (and 2.0?);
 * ability to run programs as an administrator.
 * 
 * To run:
 * 
 * Use DNSInterceptConsole.exe from C:\AltnetDNSIntercept\
 * 
 * Delete this-is-my-local-machine.txt to use remote Alternate Net name server.
 * This is just there because I develop on the same machine as the name server.
 * 
 * First, open one instance of Firefox web browser.
 * Run the console program AS AN ADMINISTRATOR and this will inject DNSIntercept.dll into the
 * browser process, to hook GetAddrInfoW() calls (query a DNS server to get the
 * IP address/es associated with a given host name).
 * 
 * Any standard, ICANN web sites are passed on to regular DNS servers.
 * 
 * For queries for .altnet web sites:
 * First, we find the IP of the Alternate Net name server
 * (which has a dynamic IP address, cached for 600 seconds);
 * then the .altnet query is redirected to the Alternate Net name server/s.
 * 
 * Positive results or negative error responses are returned to Firefox's
 * original GetAddrInfoW() call.
 * 
 * ICANN results with getAdrrInfoW() work fine.
 * 
 * But, there's something very wrong (probably a lot of things) with AltNetGetAddrW()
 * used to call our custom domain names. If I return the results as an IntPtr, Firefox
 * crashes every time. If I set results to IntPtr.Zero, there is no crash.
 * 
 * If possible, keep the solution at:
 * Visual Studio 2008,
 * so that I can still open it in SharpDevelop 3 (or 4).
 * 
 * Please try to keep the target framework either at NET 2.0 or NET 3.5.
 * 
 * Also, please retain the ability (or option) to look up the IP address
 * of the custom DNS server given a host name.
 * 
 * Apart from the crash issue, we also need to sanitize input for security purposes; etc,
 * and not leave that for the DNS server to have to handle.
 * 
 */

using System;
using System.Collections.Generic;
using System.Configuration;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Remoting;
using System.Text;
using EasyHook;

namespace DNSInterceptConsole
{
	public class InterceptInterface : MarshalByRefObject
    {
		public void IsInstalled(Int32 InClientPID)
        {
            Console.WriteLine(".altnet DNS Intercept has been installed in target {0}.", InClientPID);
        }

        public void OnInterceptDNS(Int32 InClientPID, String[] InHosts)
        {
            for (int i = 0; i < InHosts.Length; i++)
            {
                Console.WriteLine(InHosts[i]);
            }
        }
        
        public void OnMessage(String message)
        {
            Console.WriteLine(message);
        }

        public void ReportException(Exception InInfo)
        {
            Console.WriteLine("The target process has reported an error:\r\n" + InInfo.ToString());
        }

        public void Ping()
        {
        }
    }

    class Program
    {
       	static string AltNetBrowserProcessName = System.Configuration.ConfigurationManager.AppSettings.Get("AltNetBrowserProcessName");
    	static string AltNetHost = System.Configuration.ConfigurationManager.AppSettings.Get("AltNetHost");
       	static String ChannelName = null;
    	static string version_short = "";
    	static string version = "";

        static void Main(string[] args)
        {	
        	version = AppVersion();
			version_short = AppVersionShort();
        	Console.WriteLine("Starting .altnet DNS Lookup Intercept, v" + version + ".");
        	Int32 TargetPID = 0;
  
        	// Check software version.
        	int rand = RandomNumber(100000,999999);
        	string vurl = "http://" + AltNetHost + "/alternatenet/injector-version.php?v=" + version_short + "&r=" + rand.ToString();
        	
        	HttpWebResponse response = null;
        	Stream dataStream = null;
        	StreamReader reader = null;
        		
        	try
        		{
        		Console.WriteLine("Checking injector software version.");
        		HttpWebRequest myReq =
				(HttpWebRequest)WebRequest.Create(vurl);
        		myReq.UserAgent = "Alternatenet/1.0 version check +http://sherpoint.mooo.com:8888/alternatenet/";
        		response = (HttpWebResponse)myReq.GetResponse ();
        		
        		if (response.StatusCode == HttpStatusCode.OK)
        			{
		            // Get the stream containing content returned by the server.
		            dataStream = response.GetResponseStream ();
		            // Open the stream using a StreamReader for easy access.
		            reader = new StreamReader (dataStream);
		            // Read the content. 
		            string responseFromServer = reader.ReadToEnd ();
	        	
		            reader.Close ();
		            dataStream.Close ();
		            response.Close ();
		            
		            if (responseFromServer.IndexOf("!OK!") >= 0)
		            	{
		            	// Software does not need updating.
		            	// NOP, carry on.
		            	Console.WriteLine("OK, the DLL injector software is up-to-date.");
		            	}
		           	else if (responseFromServer.IndexOf("!UPDATE!") >= 0)
		           		{
		           		// Software needs updating. Quit the program to make the user update.
		           		// We need this as we may have changed name server domain name; etc.
		           		Console.WriteLine("You need to update the DLL injector software\r\nat sherpoint.mooo.com/alternatenet/");
		           		Console.WriteLine("Press control-c to exit the console.");
	            		Console.ReadLine();
		           		return;
		           		}
		           	else
		           		{
        				Console.WriteLine("Software version check: " + responseFromServer);
		           		}
		           	}
        		else
        			{
        			Console.WriteLine("Software version check: Server response: " + response.StatusDescription);
        			}
        		}
        	catch (System.Net.WebException wex)
        		{
        		Console.WriteLine("Software version check: Error: " + wex.Message);
        		try
        			{
        			reader.Close ();
        			}
        		catch
        			{
        			// NOP
        			}
        		try
        			{
		            dataStream.Close ();
        			}
        		catch
        			{
        			// NOP
        			}
        		try
        			{
		            response.Close ();
        			}
        		catch
        			{
        			// NOP
        			}
        		}
        		
            Console.WriteLine("Collecting open processes.");
            Process [] localByName = Process.GetProcesses();
            try
            {
            bool anyfound = false;
            bool browserfound = false;
            string ProcessName = "";
            foreach (Process p in localByName)
            	{
            	anyfound = true;
            	ProcessName = p.ProcessName;
            	// Console.WriteLine("'" + ProcessName + "','" + BrowserProcessName + "'");
            	if (ProcessName == AltNetBrowserProcessName)
            		{
            		// Try "firefox" or "chrome".
            		// Yahoo! Firefox works.
            		// OK, so now we can intercept GetAddrInfoW.
            		// If it's a .altnet address, do our own lookup
            		// and pass that back to Firefox.
            		browserfound = true;
            		TargetPID = p.Id;
            		Console.WriteLine("Found open web browser ('" + AltNetBrowserProcessName + "') process.");
            		break;
            		}
            	}
            if (anyfound == false)
            	{
	            Console.WriteLine("No processes running.\r\nYou need to launch the browser ('" + AltNetBrowserProcessName + "') first.");
	            Console.WriteLine("Press control-c to exit the console.");
	            Console.ReadLine();
	            return;
            	}
            if (browserfound == false)
            	{
	            Console.WriteLine("Browser named in configuration file is not running.\r\nYou need to launch the browser ('" + AltNetBrowserProcessName + "') first.");
	            Console.WriteLine("Press control-c to exit the console.");
	            Console.ReadLine();
	            return;
            	}
            }
            catch (Exception ex)
            {
            Console.WriteLine("Exception in process enumeration:\r\n{0}", ex.ToString());
            Console.WriteLine("Press control-c to exit the console.");
            Console.ReadLine();
            return;
            }

            try
            {
                Config.Register(
                    "An app to intercept DNS lookups for .altnet domains.",
                    "DNSInterceptConsole.exe",
                    "DNSIntercept.dll");

                RemoteHooking.IpcCreateServer<InterceptInterface>(ref ChannelName, WellKnownObjectMode.SingleCall);
                
                RemoteHooking.Inject(
                    TargetPID,
                    "DNSIntercept.dll",
                    "DNSIntercept.dll",
                    ChannelName);
                
                Console.WriteLine("Press control-c to exit the console.");
            	Console.ReadLine();
            }
            catch (Exception ExtInfo)
            {
                Console.WriteLine("There was an error while connecting to target:\r\n{0}", ExtInfo.ToString());
            	Console.WriteLine("Press control-c to exit the console.");
            	Console.ReadLine();
            }
            finally
            {
            // NOP.
            }
        }
        
	static string AppVersionShort()
		{
		try
			{
			string v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.Major.ToString() + "." + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.Minor.ToString();
			return v;
			}
		catch
			{
			return "Unknown";
			}
		}
	
	static string AppVersion()
		{
		try
			{
			string v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.Major.ToString() + "." + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.Minor.ToString() + "." + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.Build.ToString() + "." + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.Revision.ToString();
			return v;
    		}
		catch
			{
			return "Unknown";
			}
		}
    
	static int RandomNumber(int min, int max)
		{
		Random random = new Random();
		return random.Next(min, max);
		}
	
    }   
}