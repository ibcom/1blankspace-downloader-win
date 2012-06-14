using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Net;
using System.IO;
using System.Xml.Linq;
using HttpLibrary;
using System.Collections.Specialized;

namespace mydigitalspace
{
    public partial class frmLogin : Form
    {
        string _cookieHeader = string.Empty;
        HttpConnection http;

        public frmLogin()
        {
            InitializeComponent();
        }

        private void btnLogin_Click(object sender, EventArgs e)
        {
            Cursor = Cursors.WaitCursor;
            Dictionary<string, object> result = ProcessLogin(tbUserName.Text.Trim(), tbPassword.Text.Trim());
            if(result.ContainsKey("sid"))
            {
                Form frm = new frmDownload(result["sid"].ToString(), _cookieHeader);
                frm.Show();
                this.Hide();
            }
            else
            {
                lblErrorMessage.Visible = true;
            }
            Cursor = Cursors.Arrow;
        }

        private Dictionary<string, object> ProcessLogin(string username, string password)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();
            data.Add("logon", username);
            data.Add("password", password);

            string requestURI = "https://secure.mydigitalspacelive.com/ondemand/logon/";
            string datastring = Util.DictionaryToParameters(data);

            return HTMLHelper.POST(requestURI, datastring);
        }

        private static List<string> ParseXML(string xmlContent, string XName)
        {
            return ParseXML(xmlContent, XName, "");
        }

        private static List<string> ParseXML(string xmlContent, string XName, string Element)
        {
            List<string> List = new List<string>();

            try
            {
                if (xmlContent != string.Empty)
                {
                    XDocument xml = XDocument.Parse(xmlContent);

                    var items = xml.Descendants(XName);
                    foreach (var item in items)
                    {
                        if (Element != string.Empty)
                        {
                            List.Add(item.Element(Element).Value);
                        }
                        else
                        {
                            List.Add(item.Value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Write(ex.Message);
            }

            return List;
        }
    }
}
