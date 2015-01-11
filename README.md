# Web browser custom DNS
This C#.NET project injects / hooks a DLL into a web browser's GetAddrInfoW() to use custom DNS. You can set up the app to use custom DNS on all domains or on a single domain, and you can point it at a DNS server that handles non-ICANN domains.

Firstly, a dislaimer: I'm a total newbie at GitHub and also know very little about C#.NET (I usually write in VB.NET, PHP, JS; etc). So please be patient with me. If you're looking for something that will work straight out of the box, then sorry, this app is not what you're looking for. However, if you're willing to rip the guts out of it, then it might be right up your street.

And secondly, at the moment, there's a big "but": As it stands, while regular DNS lookups via the injected DLL work, if you make a custom call to a DNS server, the results always crash Firefox.

## What it should do

This C#.NET project hooks a DLL into GetAddrInfoW() on a single running instance of the Firefox web browser (and it may also be made to work with Chrome).

As it stands, regular ICANN domain names are resolved to IP addresses by making another call to GetAddrInfoW() from within the injected DLL and passing back the results to the web browser. That bit works okay.

However, GetAddrInfoW() doesn't allow you to specify a custom DNS server. To do that, the DLL has to make a different call to the DNS server using a DNS class's GetARecords() and DnsQuery() functions. Again, the results are passed back to the web browser. What happens, unfortunately, is that these results crash Firefox. So we really need someone to fix that who knows how to handle and marshal unmanaged code and convert between head-scratching C++ code which refers to things like PWSTR and char* and C# code which uses things like string and IntPtr.

If someone types in a domain like "home.altnet" into their web browser, regular DNS servers will send back an NXDOMAIN response (domain not found). However, it is possible to run your own DNS server to resolve your own non-ICANN domain names. At the moment, you'll find lines in the code that I've hardwired for my own purposes to ".altnet" which you'll want to change or make more generic using the app config.

I've added a setting so that custom DNS can be used on all domains ("all") or a single domain (eg ".abc").

## Dynamic IP address

For my own purposes, I'm running my test DNS server with a dynamic IP address, so the first thing that the app does -- given a host name for the custom DNS server -- is obtain its IP address. You may well want to add an IP address (like Google DNS's 8.8.8.8) to the app config, but I'd appreciate it if you would leave this dynamic IP check in as at least an optional facility.

## App version check

At the moment, the app also checks with one of my websites to see if the version is outdated, and if so, to report this. Again, you may not want this facility, so delete away.

## Alternatives

Of course, if you want to use a custom DNS server, then you can set this system-wide, but I really wanted a system that didn't require people to have to tinker, since it's quite technical and it could mess up their internet connection. It's also possible to point one of the two system-wide DNS server entries to a local DNS proxy like Acrylic. At one point, Google Chrome had a user-defined DNS facility, but (possibly after some soul-searching and finger-wagging), they quickly removed it. I haven't seen anything else around other than the Comodo browser which allows you to use their (hardwired) secure DNS servers, and another to access the murky depths of the Dark Net. So I believe that there will be demand for this app.

As a sidenote, Google have also made it difficult for users to access ad-hoc mesh networks on Android devices, even though such networks have proved useful in crises and emergencies. It seems -- no doubt for their own good reasons -- that they don't want people to work "off the grid". You can find out more about the philosophy and context behind developing this project in a recent blog post about [off-the-grid networking](http://mystical-faction.blogspot.co.uk/2015/01/technical-off-grid-networking-project.html).

## Requirements

Project development requires a C#.NET IDE such as SharpDevelop 3/4 or Visual Studio C#. Running it requires a Windows machine or VM (at least XP), and .NET framework 3.5 (perhaps also 2.0) runtime. Though a lot of .NET projects work on Linux boxes using Mono, this project requires imports from Windows DLLs including ws2_32 and dnsapi. The project requires elevated administrator access on the Windows machine.

## Installation

You should find all the referenced DLLs and the console executbale in the console's /bin/debug/ folder. It builds okay with SharpDevelop 3 (and hopefully with Visual Studio C#). The console executable and the intercept DLL will probably be outdated, so build before running the console.

## Running the injector

First, launch a single instance of the Firefox web browser. Then either run the app in an IDE as an administrator or run the console as an administrator. If you don't do this, then you'll get an error report telling you that the EasyHook assembly could not be registered / installed in the GAC.

Wait until the console reports that the DLL has been successfully injected, then try out a few addresses in the browser.

If you want to check out non-ICANN domain names, then you'll need to set up a DNS server on your LAN and point this app at that. The hosts and IPs in the app config file point to my test sites, but most of the time these are offline.

## Development

Apologies in advance for the many issues with this project. Apart from the crash issue, we also need to sanitize input for security purposes; etc, and not leave that for the DNS server to have to handle. If nothing else, you can maybe use the spare parts to start your own project.

If possible, please keep the solution at Visual Studio 2008, so that I can still open it in SharpDevelop 3 (or 4), and try to keep the target framework either at NET 2.0 or NET 3.5, or I won't be able to work on the project.

Many thanks. Enjoy!
Eric T.

[My Facebook profile](https://www.facebook.com/eric.twose).
