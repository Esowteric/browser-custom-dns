/*
 * Created by SharpDevelop.
 * User: ET
 * Date: 01/12/2012
 * Time: 11:57
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
 
using EasyHook; 
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Drawing;
//using System.Runtime.Remoting;
//using System.Runtime.Remoting.Channels;
//using System.Runtime.Remoting.Channels.Ipc;
using System.Threading;
using System.Windows.Forms;

namespace AltNetDNSIntercept
{
	/// <summary>
	/// Description of MainForm.
	/// </summary>
	public partial class MainForm : Form
	{
		public string AlternateNetWebSite = System.Configuration.ConfigurationManager.AppSettings.Get("AlternateNetWebSite");
		
		public MainForm()
		{
			//
			// The InitializeComponent() call is required for Windows Forms designer support.
			//
			InitializeComponent();
			
			//
			// TODO: Add constructor code after the InitializeComponent() call.
			//
		}
		
		void ExitToolStripMenuItemClick(object sender, EventArgs e)
		{
			Application.Exit();
		}
		
		void HelpAboutToolStripMenuItemClick(object sender, EventArgs e)
		{
		string v = AppVersion();
		 MessageBox.Show(".altnet DNS lookup intercept\r\n\r\nVersion: " + v + "\r\n\r\nHome page: http://shadowlands.dyndns.org/alternatenet/", ".altnet DNS Lookup Intercept", MessageBoxButtons.OK, MessageBoxIcon.Information);
		}
		
		void AlternateNetWebSiteToolStripMenuItemClick(object sender, EventArgs e)
		{
		try
			{
			System.Diagnostics.Process.Start(AlternateNetWebSite);
			}
		catch
			{
			MessageBox.Show("Could not launch the Alternate Net home page in your default web browser.", ".altnet DNS Lookup Intercept", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}
		
		public string AppVersion()
		{
		try
			{
			string v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.Major.ToString() +
				"." + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.Minor.ToString() +
				"." + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.Build.ToString() +
				"." + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.Revision.ToString();
			return v;
			}
		catch
			{
			return "Unknown";
			}
		}
		
		void ButtonStartClick(object sender, EventArgs e)
		{
		try
			{
			EasyHook.RemoteHooking
			}
		catch (System.Exception excep)
			{
			MessageBox.Show("Error in start(): " + excep.Message, ".altnet DNS Lookup Intercept", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}
		
		void ButtonExitClick(object sender, EventArgs e)
		{
			Application.Exit();
		}
	}
}
