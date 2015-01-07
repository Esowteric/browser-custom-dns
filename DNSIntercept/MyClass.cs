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

using EasyHook;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Remoting;
using System.Security;
using System.Text;
using System.Threading;

namespace DNSIntercept
{
	public unsafe class Main : EasyHook.IEntryPoint
	    {
		[StructLayout(LayoutKind.Sequential)]
		unsafe struct SockAddr {
		    public ushort sa_family;
		    public fixed byte sa_data[14]; // Note: sizeof(char) == 2 in C#
		}
		
		[StructLayout(LayoutKind.Sequential)]
		unsafe struct SockAddr_In {
		    public short sin_family;
		    public ushort sin_port;
		    public In_Addr sin_addr;
		    public fixed byte sin_zero[8];
		}
		
		[StructLayout(LayoutKind.Sequential)]
		struct In_Addr {
		    public byte s_b1, s_b2, s_b3, s_b4;
		}
		
		internal unsafe struct AddressInfoW {
	        internal AddressInfoHints ai_flags;
	       	internal int ai_family;
	        internal int ai_socktype;
	        internal int ai_protocol;
	        internal uint ai_addrlen;
	        internal char* ai_canonname;   // C++ was PWSTR. Ptr to the canonical name - char IS 2 bytes wide (Unicode) in C#.NET.
	        internal byte* ai_addr;         // Ptr to the sockaddr structure
	        internal AddressInfoW* ai_next;  // Ptr to the next AddressInfo structure. In our case, we just make it null.
	  		}
		
		internal enum AddressInfoHints {
			AI_PASSIVE     = 0x01, /* Socket address will be used in bind() call */
	        AI_CANONNAME   = 0x02, /* Return canonical name in first ai_canonname */
	        AI_NUMERICHOST = 0x04, /* Nodename must be a numeric address string */
	    	}
		
		const int AF_INET = 2;
		const int AF_INET6 = 23; 
		const int AF_UNSPEC = 0;
		const int IPPROTO_TCP = 6;
		const int IPPROTO_UDP = 17;
		const int SOCK_STREAM = 1;
		const int SOCK_DGRAM = 2;
		
    	static DNSInterceptConsole.InterceptInterface Interface;
        LocalHook InterceptDNSHook;
        
        static Stack<String> Queue1 = new Stack<String>();
        static int AltNetResult = 0;
        
        // static IntPtr pAltNetResult = new IntPtr(AltNetResult);
        
        // static AddressInfoW AltNetOut = new AddressInfoW();
        // IntPtr ptrResults = IntPtr.Zero;
        
        static string AltNetNameServerIPResult = "0.0.0.0";
        static string AltNetNameServerIP = "0.0.0.0";
        static DateTime AltNetNameServerLastCheck = DateTime.Now.AddMinutes(-60);
        static DateTime DateTimeNow = DateTime.Now;
        static DateTime DateTimeMinusFiveMinutes = DateTime.Now.AddMinutes(-5);
        static DateTime DateTimeMinusOneMinute = DateTime.Now.AddMinutes(-1);
        static string AppBasePath = System.Configuration.ConfigurationManager.AppSettings.Get("AppBasePath");
        //static string AltNetHost = System.Configuration.ConfigurationManager.AppSettings.Get("AltNetHost");
        //static string AltNetLocalServerAddress = System.Configuration.ConfigurationManager.AppSettings.Get("LocalServerAddress");
        static bool LocalMachine = false;
        static Int32 NSTimeOut = Convert.ToInt32(System.Configuration.ConfigurationManager.AppSettings.Get("NSTimeOut"));
        static Int32 AltNetNSTimeOut = Convert.ToInt32(System.Configuration.ConfigurationManager.AppSettings.Get("AltNetNSTimeOut"));

        const int WSSUCCESS = 0; // Our custom, zero success number.
        const int WSERR = 1; // Our custom, non-zero error number.
        const int WSAEWOULDBLOCK = 10035; // Temporarily unavailable.
        const int WSATYPE_NOT_FOUND = 10109; // Service type not found.
		const int WSAHOST_NOT_FOUND = 11001; // Use for NXRECORD or NOTAUTHORITATIVE.
        const int WSANO_RECOVERY = 11003; // Nonrecoverable error, use for SERVERFAILURE.
        const int WSAEINVAL = 10022; // Invalid argument, use for FORMATERROR.
        // static TextLogger.clsLog logger = new TextLogger.clsLog();
        // private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        static IPAddress IP_ADD = IPAddress.Parse("8.8.8.8");
        static IntPtr address_in_pointer = IntPtr.Zero;
        
        // =====================
        
        public Main(
            RemoteHooking.IContext InContext,
            String InChannelName)
        {
        	LocalMachine = IsLocalMachine();
			
        	// connect to host...
            Interface = RemoteHooking.IpcConnectClient<DNSInterceptConsole.InterceptInterface>(InChannelName);

            Interface.Ping();
        }

        // =====================
        
        public static string Right(string original, int numberCharacters)
        {
        	return original.Substring(original.Length - numberCharacters);
        }
        
		// =====================
		
		public static char Chr(int n)
		{
			return (char) n;
		}
		
		// =====================
        
        public void Run(
            RemoteHooking.IContext InContext,
            String InChannelName)
        {
        	// install hook...
            try
            {
                InterceptDNSHook = LocalHook.Create(
                    LocalHook.GetProcAddress("WS2_32.dll", "GetAddrInfoW"),
                    new DInterceptDNS(InterceptDNS_Hooked),
                    this);

                InterceptDNSHook.ThreadACL.SetExclusiveACL(new Int32[] { 0 });
            }
            catch (Exception ExtInfo)
            {
                Interface.ReportException(ExtInfo);

                return;
            }

            Interface.IsInstalled(RemoteHooking.GetCurrentProcessId());

            RemoteHooking.WakeUpProcess();

            // wait for host process termination...
            try
            {
            while (true)
                {
                    Thread.Sleep(500);

                    // transmit newly monitored messages ...
                    if (Queue1.Count > 0)
                    {
                        String[] Package = null;

                        lock (Queue1)
                        {
                            Package = Queue1.ToArray();

                            Queue1.Clear();
                        }

                        Interface.OnInterceptDNS(RemoteHooking.GetCurrentProcessId(), Package);
                    }
                    else
                        Interface.Ping();
                }
            }
            catch
            {
                // Ping() will raise an exception if host is unreachable
            }
            finally
            {
    		
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall,
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        delegate int DInterceptDNS(
        	[In] string nodename,
        	[In] string servicename,
        	[In] ref AddressInfoW hints,
        	[Out] out IntPtr ptrResults
            );
        
        [System.Security.SuppressUnmanagedCodeSecurityAttribute()]
        
        /* was
        [DllImport("WS2_32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true,
            CallingConvention = CallingConvention.StdCall)]
        	static extern int GetAddrInfoW(
                [In] string nodename,
                [In] string servicename, 
                [In] ref AddressInfoW hints,
                [Out] out IntPtr ptrResults
                );
       */
 		
 		[DllImport("ws2_32.dll", EntryPoint = "GetAddrInfoW", CallingConvention = CallingConvention.StdCall)]
        static extern int GetAddrInfoW(
		[In] [MarshalAs(UnmanagedType.LPWStr)] string nodename,
		[In] [MarshalAs(UnmanagedType.LPWStr)] string servicename,
		[In] ref AddressInfoW hints,
		out IntPtr ptrResults
		);
 
       	[System.Security.SuppressUnmanagedCodeSecurityAttribute()]
        [DllImport("WS2_32.dll",
            CharSet = CharSet.Unicode,
            SetLastError = true,
            CallingConvention = CallingConvention.StdCall)]
        	static extern void freeaddrinfoW(
        		[In] IntPtr info
                );

        // this is where we are intercepting all file accesses!
        static int InterceptDNS_Hooked(
        		[In] string nodename,
        		[In] string servicename,
        		[In] ref AddressInfoW hints,
        		[Out] out IntPtr ptrResults
               )
        {
            
            int result_status = 0;
        	try
            {
                Main This = (Main)HookRuntimeInfo.Callback;

                lock (Queue1)
                {
                    Queue1.Push("[" + RemoteHooking.GetCurrentProcessId() + ":" + 
                        RemoteHooking.GetCurrentThreadId() +  "]: \"" + nodename + "\"");
                }
            }
            catch
            {
            }
            
            // call original API or our custom DNS lookup
            
            if (nodename != "")
            {
            	// LogMessage("InterceptDNS_Hooked: nodename: " + nodename + ".");
            	if (nodename.IndexOf(".altnet") == nodename.Length - 7)
            	{
            		// LogMessage("===START ALTNET DOMAIN LOOKUP==================");
            		LogMessage(nodename + " is an AltNet domain.");
            		
            		// .altnet domain, do our own lookup.
            		// The results crash Firefox, but not if set to IntPtr.Zero ...
            		
            		result_status = AltNetGetAddrInfoW(
            		nodename,
            		servicename,
	                ref hints,
	                out ptrResults);
            		return result_status;
            	}
            	else
            	{
            		result_status = GetAddrInfoW(
                	nodename,
                	servicename,
                	ref hints,
                	out ptrResults);
            		return result_status;
            	}
            }
            else
            {
            LogMessage("InterceptDNS_Hooked: nodename is empty.");
            	result_status = GetAddrInfoW(
                	nodename,
                	servicename, 
                	ref hints,
                	out ptrResults);
            return result_status;
            }
        }
    
        // =====================
        
        static string char_star_to_string(char* buf)
        {
        	return Marshal.PtrToStringAnsi((IntPtr)buf);
        }
        
        // =====================
        
    	static unsafe int AltNetGetAddrInfoW(
        			[In] string nodename,
        			[In] string servicename,
        			[In] ref AddressInfoW hints,
        			[Out] out IntPtr ptrResults)
        
        {
        	string gotTo = "Start";
        	
        	try
            {
        		LogMessage("AltNetGetAddrInfoW(): Start.");
        		
        		////// AddressInfo AltNetOut = new AddressInfo();
        		
        		char* canonname = stackalloc char[100];
        		
        		for (int i = 0; i < nodename.Length; i++)
        		{
        			canonname[i] = nodename[i];
        		}
        		canonname[nodename.Length] = '\0';
        		
        		// LogMessage("=== NS LOOKUP ==================");
        		// DnsQuery_W needs to use port 53, so forward post 53 to 53534 on Raspberry Pi.
        		string AltNetHost = "sherpoint.mooo.com";
        		string AltNetLocalServerAddress = "192.168.1.65";
        		
        		int size = 0;
        		
        		// Check last name server IP update in AltNetNameServerLastCheck
				DateTimeNow = DateTime.Now;
				DateTimeMinusFiveMinutes = DateTime.Now.AddMinutes(-5);
        		DateTimeMinusOneMinute = DateTime.Now.AddMinutes(-1);
        		bool NeedToCheckNameserverIP = false;
        		
				int LastNameServerIPResult = DateTime.Compare(AltNetNameServerLastCheck, DateTimeMinusFiveMinutes);
				if (AltNetNameServerIP == "0.0.0.0")
				{
					// Last check failed.
					// Need to fetch AltNet name server address.
					// If at least 1 minute has elapsed since last fetch.
					LastNameServerIPResult = DateTime.Compare(AltNetNameServerLastCheck, DateTimeMinusOneMinute);
					if (LastNameServerIPResult > 0)
					{
						// Last check was less than one minute ago.
						
						AltNetNameServerIPResult = "1.1.1.1";
		        		
						ptrResults = IntPtr.Zero;
		        		
		        		NeedToCheckNameserverIP = false;
						AltNetResult = WSAEWOULDBLOCK;  
            			return AltNetResult;
					}
					else
					{
						NeedToCheckNameserverIP = true;
						// LogMessage("Last AltNet name server IP update failed. Need to update again.");
					}
				}
				else
				{
					if (LastNameServerIPResult < 0)
					{
					   // Last check was more than five minutes ago.
					   // Need to fetch AltNet name server address.
					   NeedToCheckNameserverIP = true;
		               // LogMessage("Last AltNet name server IP update > 5 minutes ago. Need to update again.");
					}
					else if (LastNameServerIPResult == 0)
					{
						// Last check was five minutes ago. 
						// Need to use cached name server address (if it succeeded).
						NeedToCheckNameserverIP = false;
						// LogMessage("Last AltNet name server IP update =  5 minutes ago. Need to update again.");
					}
					else if (LastNameServerIPResult > 0)
					{
						// Last check was less than five minutes ago.
						// Need to use cached name server address (if it succeeded).
						NeedToCheckNameserverIP = false;
					}
				}
				
				if (NeedToCheckNameserverIP == true)
				{
					// OK, fetch a new NeedToCheckNameserverIP.
					AltNetNameServerLastCheck = DateTimeNow;
					if (AltNetHost == null)
					{
						LogMessage("AltNet host is null: aborting name server IP lookup.");
						AltNetNameServerIPResult = "1.1.1.1";
					}
					else if (AltNetHost == "")
					{
						LogMessage("AltNet host is empty: aborting name server IP lookup.");
						AltNetNameServerIPResult = "1.1.1.1";
					}
					else
					{
						string AltNetNameServerIPResult = GetAltNetNameServerIPAddress(AltNetHost);
					
						if (AltNetNameServerIPResult == "0.0.0.0")
						{
							AltNetNameServerIP = AltNetNameServerIPResult;
							LogMessage("AltNet name server IP lookup failed (0.0.0.0 returned).");
						}
						else if (AltNetNameServerIPResult.IndexOf("Error") >= 0)
						{
							LogMessage(AltNetNameServerIPResult);
							AltNetNameServerIPResult = "1.1.1.1";
							AltNetNameServerIP = AltNetNameServerIPResult;
						}
						else
						{
						// Success.
						AltNetNameServerIP = AltNetNameServerIPResult;
						// LogMessage("AltNet name server IP success: " + AltNetNameServerIP);
						}
					}
				}
				
				if (AltNetNameServerIP == "0.0.0.0" | AltNetNameServerIP == "1.1.1.1")
				{
					LogMessage("AltNetGetAddrInfoW: IP: 0.0.0.0.");
					
					AltNetNameServerIPResult = "1.1.1.1";
	        		
					ptrResults = IntPtr.Zero;
	        		
					AltNetResult = WSAEWOULDBLOCK;  
	            	return AltNetResult;
				}
				
				if (NeedToCheckNameserverIP == true)
        		{
	        		LogMessage("Real AltNet name server IP is " + AltNetNameServerIP);
			        LocalMachine = IsLocalMachine();
	        		if (LocalMachine == true)
	        		{
	        			AltNetNameServerIPResult = AltNetLocalServerAddress;
						AltNetNameServerIP = AltNetNameServerIPResult;
	        			LogMessage("Running on local LAN, using " + AltNetNameServerIP + " as AltNet NS.");
	        		}
        		}
       		
        		// OK, now lookup IP of .altnet host, as requested.
        		// LogMessage("=== IP LOOKUP ==================");
        		
        		AltNetNameServerIPResult = LookupIPAddressUsingAltNetNS(nodename, AltNetNameServerIP);     		
        		
        		if (AltNetNameServerIPResult.IndexOf("Error") >= 0)
        		{
        			if (AltNetNameServerIPResult.IndexOf("DNS name does not exist") > 0)
        			{
        				LogMessage("AltNet NS returned NxDomain (" + nodename + " does not exist).");
        				AltNetNameServerIPResult = "1.1.1.1";
	        			
	        			ptrResults = IntPtr.Zero;
	        			
        				AltNetResult = WSAHOST_NOT_FOUND;
            			return AltNetResult;
        			}
        			else
        			{
        				LogMessage("Error looking up .altnet host: " + AltNetNameServerIPResult);
        			}
        		}
        		
        		if (AltNetNameServerIPResult == "Error: FORMATERROR")
        		{
        			AltNetNameServerIPResult = "1.1.1.1";
        			
        			ptrResults = IntPtr.Zero;
        			
        			AltNetResult = WSAEINVAL;
            		return AltNetResult;
        		}
        		else if (AltNetNameServerIPResult == "Error: NOTIMPLEMENTED")
        		{
        			AltNetNameServerIPResult = "1.1.1.1";
        			
        			ptrResults = IntPtr.Zero;
        			
        			AltNetResult = WSATYPE_NOT_FOUND;
            		return AltNetResult;
        		}
        		else if (AltNetNameServerIPResult == "Error: NOTAUTHORATIVE")
        		{
        			AltNetNameServerIPResult = "1.1.1.1";
        			
        			ptrResults = IntPtr.Zero;
        			
        			AltNetResult = WSAHOST_NOT_FOUND;
            		return AltNetResult;
        		}
        		else if (AltNetNameServerIPResult == "Error: NXDOMAIN")
        		{
        			AltNetNameServerIPResult = "1.1.1.1";
        			
        			ptrResults = IntPtr.Zero;
        			
        			AltNetResult = WSAHOST_NOT_FOUND;
            		return AltNetResult;
        		}
        		else if (AltNetNameServerIPResult == "Error: SERVERFAILURE")
        		{
        			AltNetNameServerIPResult = "1.1.1.1";
        			
        			ptrResults = IntPtr.Zero;
        			
        			AltNetResult = WSAEWOULDBLOCK;
            		return AltNetResult;
        		}
        		else if (AltNetNameServerIPResult == "0.0.0.0" | AltNetNameServerIPResult == "1.1.1.1")
					{
					LogMessage("AltNet domain IP lookup failed (0.0.0.0 returned).");
					AltNetNameServerIPResult = "1.1.1.1";
        			
					ptrResults = IntPtr.Zero;
					
        			AltNetResult = WSAHOST_NOT_FOUND;
            		return AltNetResult;
        			}
        		else if (AltNetNameServerIPResult.IndexOf("Error") >= 0)
        			{
        			LogMessage("AltNetGetAddrInfoW, catch all other errors. Returning DNS ServerError. IP: '" + AltNetNameServerIP + "'.");
        			AltNetNameServerIPResult = "1.1.1.1";
        			
        			ptrResults = IntPtr.Zero;
        			
        			AltNetResult = WSAEWOULDBLOCK;
            		return AltNetResult;
        			}
        		
				// OK, success.
        		size = 0;
        		
        		if (address_in_pointer != IntPtr.Zero)
        		{
        			// Marshal.FreeHGlobal(address_in_pointer);
        		}
        		
        		// AltNetOut = new AddressInfo();
        		
        		if (AltNetNameServerIPResult.IndexOf(":") >= 0)
        		{
        			LogMessage("DNS server returned an IPv6 address.");
        			// hints_family = AF_INET6;
        		}
        		if (AltNetNameServerIPResult.IndexOf(".") >= 0)
        		{
        			LogMessage("DNS server returned an IPv4 address.");
        		}
        		else
        		{
        			LogMessage("DNS server failed to return an IPv4 or IPv6 address.");
        			AltNetNameServerIPResult = "1.1.1.1";
        			ptrResults = IntPtr.Zero;
        			AltNetResult = WSATYPE_NOT_FOUND;
            		return AltNetResult;
        		}
        		
        		gotTo = "Near end";
        		
        		AddressInfoW AltNetOut = new AddressInfoW();
        		
        		AltNetOut.ai_canonname = canonname;
	        	AltNetOut.ai_family = AF_INET;
	        	AltNetOut.ai_flags = AddressInfoHints.AI_CANONNAME;
	        	AltNetOut.ai_protocol = IPPROTO_TCP;
	        	AltNetOut.ai_socktype = SOCK_STREAM;
        		address_in_pointer = CreateSockaddrInStructure(AltNetNameServerIPResult, out size);	    
        		AltNetOut.ai_addr = (byte*)address_in_pointer;
        		AltNetOut.ai_addrlen = (uint)size;
			    AltNetOut.ai_next = null;
			    
			    LogMessage("AltNetGetAddrInfoW: Marshalling results.");
			    
        		ptrResults = Marshal.AllocHGlobal(Marshal.SizeOf(AltNetOut));
        		Marshal.StructureToPtr(AltNetOut, ptrResults, false);
        		
// Firefox doesn't crash if we use the standard GetAddrInfoW()
// or if we set ptrResults to IntPtr.Zero:
// ptrResults = IntPtr.Zero;

				LogMessage("AltNetGetAddrInfoW: Returning successful response.");	
        		AltNetResult = 0;
            	return AltNetResult;
            }
        	catch (Exception ex)
            {
            // Not sure how to assign ptrResults, so maybe let GetAddrInfoW() do that
            // and return the correct data for an error?
            
           	LogMessage("Error in AltNetGetAddrInfoW(): " + ex.Message + " (" + gotTo + ").");
            
           	return GetAddrInfoW(
                nodename,
                servicename, 
                ref hints,
                out ptrResults);
            }
        }
        
        // =====================
        
        static string GetAltNetNameServerIPAddress(string nodename)
        {
        	try
        	{
        		LogMessage("Looking up AltNet name server: " + nodename);
        		AltNetNameServerIP = GetIpFromHostname(nodename);
        		// LogMessage("GetAltNetNameServerIPAddress. GetIpFromHostname returned: " + AltNetNameServerIP);
        		if (AltNetNameServerIP.IndexOf("Error: ") == 0)
				{
					return "Error: AltNet name server IP lookup failed: " + AltNetNameServerIP;
				}
				else
				{
					return AltNetNameServerIP;
				}
			
        	}
        	catch (Exception ex)
        	{
        		LogMessage("GetAltNetNameServerIPAddress error. IP from GetIpFromHostname was: " + AltNetNameServerIP);
        		return "Error in GetAltNetNameServerIPAddress(): " + ex.Message;
        	}
        }

     // =====================   
        
	 static string LookupIPAddressUsingAltNetNS(string nodename, string AltNetNameServerIP)
        {
        	string ip_string = "0.0.0.0";
	 		try
        	{
        		// LogMessage("LookupIPAddressUsingAltNetNS: start: looking up: " + nodename + ", using NS at " + AltNetNameServerIP + ".");
        		
        		string[] s = DnsA.GetARecords(nodename,  AltNetNameServerIP);
        		
        		int listlen = s.Length;
				// LogMessage("LookupIPAddressUsingAltNetNS. Entries found: " + listlen.ToString());
        		
				foreach (string st in s)
				{
					ip_string = st;
					break;
					//Console.ReadLine(); 
				}
				
				if (ip_string.IndexOf(":") >= 0)
				{
					return "Error in LookupIPAddressUsingAltNetNS(): We can only handle IPv4 hosts. IPv6 address returned for " + nodename + ".";	
				}
				
        		LogMessage("LookupIPAddressUsingAltNetNS, success: IP: '" + ip_string + "'.");
				return ip_string;
        	}
        	catch (Exception ex)
        	{
        		// LogMessage("LookupIPAddressUsingAltNetNS, error: IP: " + ip_string + ".");
        		return "Error in LookupIPAddressUsingAltNetNS(): " + ex.Message;
        	}
        }
    
	 // =====================
	 
	 // Send a message to queue and display in the console.
	 public static void LogMessage(string text)
	 {
	 	try
	 	{
	 		Interface.OnMessage("MSG: " + text);
	 	}
	 	catch
	 	{
	 	// NOP.	
	 	}
	 }
	 
	// =====================
	 
	static byte[] GetBytes(string str)
	{
	    byte[] bytes = new byte[str.Length * sizeof(char)];
	    System.Buffer.BlockCopy(str.ToCharArray(), 0, bytes, 0, bytes.Length);
	    return bytes;
	}
	
	// =====================
	
	static string GetString(byte[] bytes)
	{
	    char[] chars = new char[bytes.Length / sizeof(char)];
	    System.Buffer.BlockCopy(bytes, 0, chars, 0, bytes.Length);
	    return new string(chars);
	}
	
	// =====================
	
	static string GetIpFromHostname(string host)
	{
		string ip = "0.0.0.0";
		try
		{
			// LogMessage("GetIpFromHostname. Looking up: " + host);
			IPAddress[] addresslist = Dns.GetHostAddresses(host);
		
			int listlen = addresslist.Length;
			// LogMessage("GetIpFromHostname. Entries found: " + listlen.ToString());
			
			foreach (IPAddress theaddress in addresslist)
			{
				ip = theaddress.ToString();
				// LogMessage("GetIpFromHostname. Entries found: " + ip);
				break;
			}
			// LogMessage("GetIpFromHostname. Success, returning: " + ip);
			return ip;
		}
		catch (Exception ex)
	 	{
	 		LogMessage("GetIpFromHostname. Error, returning: " + ip);
			return "Error in GetIpFromHostname: " + ex.Message;
	 	}
		
	}
	
		// =====================
	
		// This is sockaddr. We don't want to use this.
		// From server to Win, we want to use sockaddr_in.
		/// <summary>
        /// Creates an unmanaged sockaddr structure to pass to a WinAPI function.
        /// </summary>
        /// <param name="ipEndPoint">IP address and port number</param>
        /// <returns>a handle for the structure. Use the AddrOfPinnedObject Method to get a stable pointer to the object. </returns>
        /// <remarks>When the handle goes out of scope you must explicitly release it by calling the Free method; otherwise, memory leaks may occur. </remarks>
        private static GCHandle CreateSockaddrStructure(IPEndPoint ipEndPoint, out int size)
        {
	       	SocketAddress socketAddress = ipEndPoint.Serialize();
	        size = socketAddress.Size;
	            
	        // use an array of bytes instead of the sockaddr structure 
	        byte[] sockAddrStructureBytes = new byte[socketAddress.Size];
	        GCHandle sockAddrHandle = GCHandle.Alloc(sockAddrStructureBytes, GCHandleType.Pinned);
	        for (int i = 0; i < socketAddress.Size; ++i)
	        {
	            sockAddrStructureBytes[i] = socketAddress[i];
	        }
	        return sockAddrHandle;   	
        }
	
        // =====================
        
        private static IntPtr CreateSockaddrInStructure(string ip_address, out int size)
        {
        	try
        	{
        	// LogMessage("CreateSockaddrInStructure: Creating structure for: " + ip_address + ".");
        		
        	// The SOCKADDR_IN structure is built as if it were on a little-endian machine
        	// and is treated as a byte array. For more information, see [SOCKADDR].
        	// little-endian: Multiple-byte values that are byte-ordered with the
        	// least significant byte stored in the memory location with the lowest address. 
 
        	SockAddr_In s; // new SockAddr_In();
		    SockAddr_In* ps = &s;
		    SockAddr_In* psa = (SockAddr_In*)ps;
		    
		    var inAddr = new In_Addr();
		
		    string[] ip_bits = ip_address.Split(new Char [] {'.'});
		    System.Text.ASCIIEncoding encoding = new System.Text.ASCIIEncoding();
		    
		    inAddr.s_b1 = System.Convert.ToByte(ip_bits[0]);
		    inAddr.s_b2 = System.Convert.ToByte(ip_bits[1]);
		    inAddr.s_b3 = System.Convert.ToByte(ip_bits[2]);
		    inAddr.s_b4 = System.Convert.ToByte(ip_bits[3]);
		    psa->sin_addr = inAddr;
		    psa->sin_family = AF_INET;
		    psa->sin_port = 0;
		    for (var i=0;i<8;i++)
		    {
				psa->sin_zero[i] = 0;
		    }
				
	        size = Marshal.SizeOf(s);
	        
        	address_in_pointer = Marshal.AllocHGlobal(size);
		    Marshal.StructureToPtr(s, address_in_pointer, false);

		    // Let's see if this is the operation that crashes Firefox ....
		    // No, returning IntPtr.Zero makes no difference;
		    
	        return address_in_pointer;   	
        }
        catch (Exception ex)
        {
        	LogMessage("Error in CreateSockaddrInStructure: " + ex.Message);
        	size = 0;
        	return IntPtr.Zero;
        }
        }
        
        // =====================
        
        static bool IsLocalMachine()
        {
        	if (File.Exists("C:\\AltnetDNSIntercept\\this-is-my-local-machine.txt"))
        	{
				LocalMachine = true;
        	}
			else
			{
				LocalMachine = false;
			}
		return LocalMachine;
        }
        
    } // end of class
	
// ====================================================================
// ====================================================================

 public class DnsA
{
public DnsA()
{
}
[DllImport("dnsapi", EntryPoint="DnsQuery_W", CharSet=CharSet.Unicode, SetLastError=true, ExactSpelling=true)]
private static extern int DnsQuery([MarshalAs(UnmanagedType.VBByRefStr)]ref string pszName, QueryTypes wType, QueryOptions options, ref IP4_ARRAY dnsServerIpArray, ref IntPtr ppQueryResults, int pReserved);

[DllImport("dnsapi", CharSet=CharSet.Auto, SetLastError=true)]
private static extern void DnsRecordListFree(IntPtr pRecordList, int FreeType);

public static string[] GetARecords(string domain, string NSIP)
{
// DNSIntercept.Main.LogMessage("GetARecords: start. Domain: '" + domain + "', NS: '" + NSIP + "'.");
	
IntPtr ptr1=IntPtr.Zero ;
IntPtr ptr2=IntPtr.Zero ;
ARecord recA;
if (Environment.OSVersion.Platform != PlatformID.Win32NT)
{
throw new NotSupportedException();
}
ArrayList list1 = new ArrayList();

uint address = BitConverter.ToUInt32(IPAddress.Parse(NSIP).GetAddressBytes(), 0);
// This is OK: DNSIntercept.Main.LogMessage("GetARecords: NS: " + NSIP + "=" + address.ToString());
uint[] ipArray = new uint[1];
ipArray.SetValue(address, 0);
IP4_ARRAY dnsServerArray = new IP4_ARRAY();
dnsServerArray.AddrCount = 1;
dnsServerArray.AddrArray = new uint[1];
dnsServerArray.AddrArray[0] = address;

int num1 = DnsA.DnsQuery(ref domain, QueryTypes.DNS_TYPE_A, QueryOptions.DNS_QUERY_BYPASS_CACHE, ref dnsServerArray, ref ptr1, 0);
if (num1 != 0)
{
// OK, this will throw errors like DNS host not found,
// for an NXDomain response from DNS server
// (which we trap anyhow, using a match in the error string).

throw new Win32Exception(num1);
}
long lipadd = 0;
int cnt = 1;
for (ptr2 = ptr1; !ptr2.Equals(IntPtr.Zero); ptr2 = recA.pNext)
	{
	recA = (ARecord) Marshal.PtrToStructure(ptr2, typeof(ARecord));
	
	if (recA.wType == 1)
		{
			if (cnt == 1)
			{
				lipadd = recA.wIPAddress;
				if (lipadd == 0)
				{
					// DNSIntercept.Main.LogMessage("GetARecords: DNSQuery for .altnet domain returned 0.0.0.0.");
				}
				string text1 =long2ip(lipadd);
				list1.Add(text1);
				// DNSIntercept.Main.LogMessage("GetARecords: Adding: " + text1);
			}
		}
	cnt = cnt + 1;
	}
DnsA.DnsRecordListFree(ptr2, 0);
return (string[]) list1.ToArray(typeof(string));
}

private enum QueryOptions
{
DNS_QUERY_ACCEPT_TRUNCATED_RESPONSE = 1,
DNS_QUERY_BYPASS_CACHE = 8,
DNS_QUERY_DONT_RESET_TTL_VALUES = 0x100000,
DNS_QUERY_NO_HOSTS_FILE = 0x40,
DNS_QUERY_NO_LOCAL_NAME = 0x20,
DNS_QUERY_NO_NETBT = 0x80,
DNS_QUERY_NO_RECURSION = 4,
DNS_QUERY_NO_WIRE_QUERY = 0x10,
DNS_QUERY_RESERVED = -16777216,
DNS_QUERY_RETURN_MESSAGE = 0x200,
DNS_QUERY_STANDARD = 0,
DNS_QUERY_TREAT_AS_FQDN = 0x1000,
DNS_QUERY_USE_TCP_ONLY = 2,
DNS_QUERY_WIRE_ONLY = 0x100
}

private enum QueryTypes
{
DNS_TYPE_A = 1
}

[StructLayout(LayoutKind.Sequential)]
public struct ARecord
{
public IntPtr pNext;
public string pName;
public short wType;
public short wDataLength;
public int flags;
public int dwTtl;
public int dwReserved;
// No! public long wIPAddress;
public int wIPAddress;
}

[StructLayout(LayoutKind.Sequential)]
public struct IP4_ARRAY
{
/// DWORD->unsigned int
public UInt32 AddrCount;

/// IP4_ADDRESS[1]
[MarshalAs(UnmanagedType.ByValArray, SizeConst = 1, ArraySubType = UnmanagedType.U4)] public UInt32[] AddrArray;
}

static public string long2ip( long longIP )
{
try
	{
	// DNSIntercept.Main.LogMessage("long2ip: longip=" + longIP.ToString() + ").");
	return new IPAddress( longIP ).ToString();
	}
catch // (Exception ex)
	{
	// DNSIntercept.Main.LogMessage("Error in long2ip: " + ex.Message + " (longip=" + longIP.ToString() + ").");
	return "0.0.0.0";
	}
}

} // end of class
	
// ====================================================================
// ====================================================================
 
}
