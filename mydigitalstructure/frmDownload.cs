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
using System.Threading;
using System.Xml;
using System.Xml.XPath;
using System.Collections.Specialized;
using HttpLibrary;
using System.Reflection;
using System.Collections;
using System.Configuration;

namespace mydigitalspace
{
    public partial class frmDownload : Form
    {
        string _sid = string.Empty;
        string _cookieHeader = string.Empty;
        bool _cancelled = false;
        bool _complete = false;
        Thread th;
        HttpConnection http;
        public DirectoryInfo diRoot;
        Dictionary<string, string> hash = new Dictionary<string, string>();

        private class Folder
        {
            public string id { get; set; }
            public string title { get; set; }
        }
        private class Attachment
        {
            public string id { get; set; }
            public string type { get; set; }
            public string typetext { get; set; }
            public string filename { get; set; }
            public string createddate { get; set; }
            public string modifieddate { get; set; }
            public string link { get; set; }
            public string attachment { get; set; }
        }
        public class Document
        {
            public string title { get; set; }
            public string type { get; set; }
            public string id { get; set; }
        }


        public frmDownload(string sid, string cookieHeader)
        {
            InitializeComponent();

            //fill folder navigation
            string requestURI = "https://secure.mydigitalspacelive.com/ondemand/document/?method=DOCUMENT_FOLDER_SEARCH&rows=1&rf=XML&sid=" + sid;
            string HTML = HTMLHelper.ReadHTMLPage(requestURI);
            List<Folder> folders = ParseXML(HTML, "row");

            _sid = sid;
            _cookieHeader = cookieHeader;

            TreeNode tnRoot = new TreeNode();
            tnRoot.Text = folders[0].title;
            tnRoot.Tag = folders[0].id;
            tvFolders.Nodes.Add(tnRoot);
            tnRoot.Nodes.Add("DUMMY");
        }

        private List<Folder> FillSubFolders(string id)
        {
            string requestURI = string.Format("https://secure.mydigitalspacelive.com/ondemand/document/?method=DOCUMENT_FOLDER_SEARCH&rows=1000&parentfolder={0}&rf=XML&sid={1}", id, _sid);
            string HTML = HTMLHelper.ReadHTMLPage(requestURI);
            List<Folder> folders = ParseXML(HTML, "row");

            return folders;
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            folderBrowserDialog1.ShowDialog();
            tbFolder.Text = folderBrowserDialog1.SelectedPath;
        }

        private void btnClose_Click(object sender, EventArgs e)
        {
            try
            {
                http.Dispose();
                http = null;
            }
            catch (Exception ex)
            {
            }
            finally
            {
                this.Close();
                Application.Exit();
            }
        }

        private void btnDownload_Click(object sender, EventArgs e)
        {
            if (btnDownload.Text.Equals("Download"))
            {
                if (tbSelected.Text.Length > 0 && tbFolder.Text.Length > 0)
                {
                    string message = string.Format("You are about to download files from \n{0}\nto\n{1}\nAll files within {1} will be deleted and files from {0} will be downloaded.\nContinue?", tbSelected.Text, tbFolder.Text);
                    if (MessageBox.Show(message, "Confirm Download?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == System.Windows.Forms.DialogResult.Yes)
                    {
                        //begin Download process
                        _cancelled = false;
                        _complete = false;
                        btnClose.Enabled = false;
                        btnBrowse.Enabled = false;
                        btnDownload.Text = "Cancel";
                        this.Refresh();
                        timer1.Enabled = true;
                        timer1.Start();

                        //initialize hash
                        hash = new Dictionary<string, string>();
                        hash.Add("+jul", "01 ");
                        hash.Add("+aug", "02 ");
                        hash.Add("+sep", "03 ");
                        hash.Add("+oct", "04 ");
                        hash.Add("+nov", "05 ");
                        hash.Add("+dec", "06 ");
                        hash.Add("+jan", "07 ");
                        hash.Add("+feb", "08 ");
                        hash.Add("+mar", "09 ");
                        hash.Add("+apr", "10 ");
                        hash.Add("+may", "11 ");
                        hash.Add("+jun", "12 ");

                        th = new Thread(new ThreadStart(ProcessDownload));
                        th.IsBackground = false;
                        th.Start();
                    }
                }
                else
                {
                    MessageBox.Show("You need to select a folder first before continuing.", "No Folder Selected!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                //stop Download process
                _cancelled = true;
                btnClose.Enabled = true;
                btnBrowse.Enabled = true;
                btnDownload.Text = "Download";
                SetControlPropertyThreadSafe(lblStatus, "Text", "Cancelled");
            }
        }

        private static List<Folder> ParseXML(string xmlContent, string XName)
        {
            return ParseXML(xmlContent, XName, "");
        }

        private static List<Folder> ParseXML(string xmlContent, string XName, string Element)
        {
            List<Folder> list = new List<Folder>();

            try
            {
                if (xmlContent != string.Empty)
                {
                    XDocument xml = XDocument.Parse(xmlContent);

                    var items = from x in xml.Descendants(XName)
                                  select new
                                  {
                                      id = x.Descendants("id").First().Value,
                                      title = x.Descendants("title").First().Value
                                  };
                    foreach(var item in items)
                    {
                        Folder f = new Folder();
                        f.id = item.id;
                        f.title = item.title;
                        list.Add(f);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Write(ex.Message);
            }

            return list;
        }

        private void Process(string FolderPath, Folder folder)
        {
            SetControlPropertyThreadSafe(lblStatus, "Text", string.Format("Creating Folder {0}", folder.title));
            DirectoryInfo di = Directory.CreateDirectory(Path.Combine(FolderPath, FormatName(folder.title)));

            //get documents
            SetControlPropertyThreadSafe(lblStatus, "Text", string.Format("Getting Documents for Folder {0}", folder.title));
            List<Document> documents = GetDocuments(folder.id);

            foreach (Document doc in documents)
            {
                //create directory in root
                DirectoryInfo diDoc = Directory.CreateDirectory(Path.Combine(di.FullName, FormatName(doc.title)));
                //if document is of type 7, download all attachments linked to the document and store them in created folder
                List<Attachment> attachments = GetAttachments(doc.id);
                DownloadAttachments(attachments, diDoc.FullName);
            }

            foreach (Folder f in FillSubFolders(folder.id))
            {
                Thread.Sleep(2000);
                Process(di.FullName, f);
            }
        }

        private void DownloadAttachments(List<Attachment> attachments, string DocPath)
        {
            foreach (Attachment a in attachments)
            {
                string requestURI = string.Format("https://secure.mydigitalspacelive.com/ondemand/core/?method=CORE_ATTACHMENT_DOWNLOAD&id={0}&sid={1}", a.id, _sid);
                SetControlPropertyThreadSafe(lblStatus, "Text", string.Format("Downloading attachment {0}", a.filename));
                DownloadFile(requestURI, Path.Combine(DocPath, a.filename));
            }
        }

        private List<Document> GetDocuments(string id)
        {
            List<Document> documents = new List<Document>();

            try
            {
                //post xml
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                sb.AppendLine("<advancedSearch>");
                sb.AppendLine("<field>");
                sb.AppendLine("<name>title</name>");
                sb.AppendLine("</field>");
                sb.AppendLine("<field>");
                sb.AppendLine("<name>type</name>");
                sb.AppendLine("</field>");
                sb.AppendLine("<filter>");
                sb.AppendLine("<name>document.documentfolderlink.folder</name>");
                sb.AppendLine("<comparison>EQUAL_TO</comparison>");
                sb.AppendLine(string.Format("<value1>{0}</value1>", id));
                sb.AppendLine("<value2/>");
                sb.AppendLine("<name>type</name>");
                sb.AppendLine("<comparison>EQUAL_TO</comparison>");
                sb.AppendLine("<value1>7</value1>");
                sb.AppendLine("<value2/>");
                sb.AppendLine("</filter>");
                sb.AppendLine("<options>");
                sb.AppendLine("<rf>json</rf>");
                sb.AppendLine("<startrow>0</startrow>");
                sb.AppendLine("<rows>1000</rows>");
                sb.AppendLine("</options>");
                sb.AppendLine("</advancedSearch>");

                string requestURI = string.Format("https://au.mydigitalstructure.com/ondemand/document/document.aspx?method=document_search&advanced=1&sid={0}", _sid);

                Dictionary<string, object> retdic = HTMLHelper.POSTXML(requestURI, sb.ToString());
                retdic = (Dictionary<string, object>)Util.DeSerializeObject<Dictionary<string, object>>(retdic["data"].ToString());
                documents = (List<Document>)Util.DeSerializeObject<List<Document>>(retdic["rows"].ToString());
                documents = documents.Where(f => f.type.Equals("7")).ToList();
            }
            catch
            {
            }

            return documents;
        }

        private List<Attachment> GetAttachments(string docid)
        {
            List<Attachment> attachments = new List<Attachment>();
            try
            {
                string requestURI = string.Format("https://secure.mydigitalspacelive.com/ondemand/core/?method=CORE_ATTACHMENT_SEARCH&rows=1000&object=14&objectcontext={0}&sid={1}", docid, _sid);
                string HTML = HTMLHelper.ReadHTMLPage(requestURI);
                Dictionary<string, object> attachmentdic = (Dictionary<string, object>)Util.DeSerializeObject<Dictionary<string, object>>(HTML);
                attachmentdic = (Dictionary<string, object>)Util.DeSerializeObject<Dictionary<string, object>>(attachmentdic["data"].ToString());
                attachments = (List<Attachment>)Util.DeSerializeObject<List<Attachment>>(attachmentdic["rows"].ToString());
            }
            catch { }

            return attachments;
        }

        private void ProcessDownload()
        {
            string Source = tbSelected.Text;
            string Destination = tbFolder.Text;
            //clear all files from folder
            DirectoryInfo diDestination = new DirectoryInfo(Destination);
            PurgeDirectory(diDestination);
            //create root folder
            diRoot = Directory.CreateDirectory(Path.Combine(diDestination.FullName, FormatName(Source)));
            //search for subfolders and create
            List<Folder> folders = FillSubFolders(tbSelected.Tag.ToString());
            List<Document> documents = GetDocuments(tbSelected.Tag.ToString());

            foreach (Document doc in documents)
            {
                //create directory in root
                DirectoryInfo diDoc = Directory.CreateDirectory(Path.Combine(diRoot.FullName, FormatName(doc.title)));
                //if document is of type 7, download all attachments linked to the document and store them in created folder
                List<Attachment> attachments = GetAttachments(doc.id);
                DownloadAttachments(attachments, diDoc.FullName);
            }
            foreach (Folder f in folders)
            {
                Process(diRoot.FullName, f);
            }

            SetControlPropertyThreadSafe(lblStatus, "Text", string.Format("Restructuring/organizing folders"));

            //additional modifications of windows folder

            foreach (DirectoryInfo d in diDestination.GetDirectories("*", SearchOption.AllDirectories))
            {
                string value = FolderMatch(d.Name, hash);
                if (value != string.Empty)
                {
                    DirectoryInfo diMonths = new DirectoryInfo(Path.Combine(d.Parent.FullName, "Months"));
                    if (!diMonths.Exists)
                    {
                        diMonths.Create();
                    }
                    //prepend value to folder name and move to Months folder
                    Directory.Move(d.FullName, Path.Combine(diMonths.FullName, value + d.Name));
                }
            }
            
            if (_cancelled == false)
            {
                _complete = true;
                SetControlPropertyThreadSafe(lblStatus, "Text", "Done");
            }
        }

        private string FolderMatch(string Folder, Dictionary<string, string> hash)
        {
            string match = string.Empty;
            foreach(KeyValuePair<string, string> kv in hash)
            {
                if (Folder.Contains(kv.Key))
                {
                    match = kv.Value;
                    break;
                }
            }

            return match;
        }

        private void PurgeDirectory(DirectoryInfo di)
        {
            foreach (DirectoryInfo diChild in di.GetDirectories())
            {
                try
                {
                    diChild.Delete(true);
                }
                catch
                {
                }
            }
            foreach (FileInfo fi in di.GetFiles())
            {
                fi.Delete();
            }
        }

        private bool DownloadFile(string URL, string SaveLocation)
        {
            try
            {
                WebClient webClient = new WebClient();
                webClient.DownloadFile(URL, SaveLocation);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private delegate void SetControlPropertyThreadSafeDelegate(Control control, string propertyName, object propertyValue);

        public static void SetControlPropertyThreadSafe(Control control, string propertyName, object propertyValue)
        {
            if (control.InvokeRequired)
            {
                control.Invoke(new SetControlPropertyThreadSafeDelegate(SetControlPropertyThreadSafe), new object[] { control, propertyName, propertyValue });
            }
            else
            {
                Label lbl = control as Label;
                lbl.Text = propertyValue.ToString();
            }
        }

        private void frmDownload_FormClosed(object sender, FormClosedEventArgs e)
        {
            try
            {
                http.Dispose();
                http = null;
            }
            catch
            {
            }
            finally
            {
                Application.Exit();
            }
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (_complete || _cancelled)
            {
                th.Abort();
                timer1.Stop();
                timer1.Enabled = false;

                btnClose.Enabled = true;
                btnBrowse.Enabled = true;
                btnDownload.Text = "Download";
            }
        }

        private void tvFolders_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            tbSelected.Text = e.Node.Text;
            tbSelected.Tag = e.Node.Tag;
        }

        private void tvFolders_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            e.Node.Nodes.Clear();
            List<Folder> folders = FillSubFolders(e.Node.Tag.ToString());

            foreach (Folder f in folders)
            {
                TreeNode tn = new TreeNode(f.title);
                tn.Tag = f.id;
                e.Node.Nodes.Add(tn);
                tn.Nodes.Add("DUMMY");
            }
        }

        private string FormatName(string Name)
        {
            return Name.Replace("&amp;", "&");
        }
    }
}
