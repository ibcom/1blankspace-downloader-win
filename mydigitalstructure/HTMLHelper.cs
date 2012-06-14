using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.IO;
using System.Xml.Linq;
using System.Drawing;
using System.Collections.Specialized;
using HttpLibrary;

namespace mydigitalspace
{
    public static class HTMLHelper
    {
        public static string ReadHTMLPage(string URL)
        {
            string HTML = "";

            try
            {
                WebResponse objResponse;
                HttpWebRequest objRequest = (HttpWebRequest)HttpWebRequest.Create(URL);
                objRequest.CookieContainer = new CookieContainer();
                objResponse = objRequest.GetResponse();
                using (StreamReader sr =
                   new StreamReader(objResponse.GetResponseStream()))
                {
                    HTML = sr.ReadToEnd();
                    // Close and clean up the StreamReader
                    sr.Close();
                }
            }
            catch
            {
            }

            return HTML;
        }

        public static string ReadHTMLPage(string URL, out string cookieHeader)
        {
            string HTML = "";

            try
            {
                WebResponse objResponse;
                HttpWebRequest objRequest = (HttpWebRequest)HttpWebRequest.Create(URL);
                objRequest.CookieContainer = new CookieContainer();
                objResponse = objRequest.GetResponse();
                cookieHeader = objResponse.Headers["Set-Cookie"];
                using (StreamReader sr =
                   new StreamReader(objResponse.GetResponseStream()))
                {
                    HTML = sr.ReadToEnd();
                    // Close and clean up the StreamReader
                    sr.Close();
                }
            }
            catch
            {
                cookieHeader = string.Empty;
            }

            return HTML;
        }

        public static string Get(string URL)
        {
            string HTML = "";
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(URL);
            request.Method = "GET";

            // Get response  
            HttpWebResponse response = (HttpWebResponse)(request.GetResponse());

            // Get the response stream  
            using (StreamReader sr =
                   new StreamReader(response.GetResponseStream()))
            {
                HTML = sr.ReadToEnd();
                // Close and clean up the StreamReader
                sr.Close();
            }

            return HTML;
        }

        public static Dictionary<string, object> POST(string URL, string PostData)
        {
            Dictionary<string, object> retdic = new Dictionary<string, object>();
            try
            {
                string ret = string.Empty;

                Uri address = new Uri(URL);
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(address);
                string encoded = HttpUtility.UrlEncode(PostData, Encoding.UTF8);
                byte[] outstream = System.Text.Encoding.UTF8.GetBytes(PostData);
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";

                // Set the content length in the request headers  
                request.ContentLength = outstream.Length;

                // Write data  
                using (Stream postStream = request.GetRequestStream())
                {
                    postStream.Write(outstream, 0, outstream.Length);
                }

                // Get response  
                HttpWebResponse response = (HttpWebResponse)(request.GetResponse());
                string responseCode = response.StatusCode.ToString();

                // Get the response stream  
                StreamReader reader = new StreamReader(response.GetResponseStream());
                ret = reader.ReadToEnd();

                retdic = (Dictionary<string, object>)Util.DeSerializeObject<Dictionary<string, object>>(ret);
            }
            catch { }

            return retdic;
        }

        public static Dictionary<string, object> POSTXML(string URL, string XML)
        {
            Dictionary<string, object> retdic = new Dictionary<string, object>();
            string ret = string.Empty;
            try
            {
                Uri address = new Uri(URL);
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(address);
                request.Method = "POST";
                //request.ContentType = @"application/atom+xml";
                //request.ContentType = @"application/x-www-form-urlencoded";
                request.ContentType = "text/xml; encoding='utf-8'";

                byte[] byteData = UTF8Encoding.UTF8.GetBytes(XML.ToString());
                //byte[] byteData = System.Text.Encoding.ASCII.GetBytes(XML.ToString());

                // Set the content length in the request headers  
                request.ContentLength = byteData.Length;

                // Write data  
                using (Stream postStream = request.GetRequestStream())
                {
                    postStream.Write(byteData, 0, byteData.Length);
                }

                // Get response  
                HttpWebResponse response = (HttpWebResponse)(request.GetResponse());

                // Get the response stream  
                StreamReader reader = new StreamReader(response.GetResponseStream());
                ret = reader.ReadToEnd();

                retdic = (Dictionary<string, object>)Util.DeSerializeObject<Dictionary<string, object>>(ret);
            }
            catch(WebException ex) { }

            return retdic;
        }

        public static string PostFile(string URL, string cookieHeader)
        {
            //string getUrl = "http://localhost/pixelpost/admin/index.php";
            //HttpWebRequest getRequest = (HttpWebRequest)HttpWebRequest.Create(getUrl);
            //getRequest.Headers.Add("Cookie", cookieHeader);
            //HttpWebResponse getResponse = (HttpWebResponse)getRequest.GetResponse();
            //using (StreamReader sr = new StreamReader(getResponse.GetResponseStream()))
            //{
            //    pageSource = sr.ReadToEnd();
            //}


            long length = 0;
            string boundary = "----------------------------" +
            DateTime.Now.Ticks.ToString("x");

            HttpWebRequest httpWebRequest2 = (HttpWebRequest)WebRequest.Create(URL);
            httpWebRequest2.ContentType = "multipart/form-data; boundary=" + boundary;
            httpWebRequest2.Method = "POST";
            httpWebRequest2.AllowAutoRedirect = false;
            httpWebRequest2.KeepAlive = false;
            httpWebRequest2.Credentials = System.Net.CredentialCache.DefaultCredentials;
            httpWebRequest2.Headers.Add("Cookie", cookieHeader);


            Stream memStream = new System.IO.MemoryStream();

            byte[] boundarybytes = System.Text.Encoding.ASCII.GetBytes("\r\n--" + boundary);


            string headerTemplate = "\r\n--" + boundary + "\r\nContent-Disposition: form-data; name=\"{0}\"; oFile0=\"{1}\"\r\nContent-Type: image/jpeg\r\n\r\n";



            string header = string.Format(headerTemplate, "userfile", "Sunset.jpg");



            byte[] headerbytes = System.Text.Encoding.ASCII.GetBytes(header);

            memStream.Write(headerbytes, 0, headerbytes.Length);


            Image img = null;
            img = Image.FromFile("D:/Documents and Settings/All Users/Documents/My Pictures/Sample Pictures/test.jpg", true);
            img.Save(memStream, System.Drawing.Imaging.ImageFormat.Jpeg);


            memStream.Write(boundarybytes, 0, boundarybytes.Length);


            string formdataTemplate = "\r\nContent-Disposition: form-data; name=\"{0}\";\r\n\r\n{1}";

            string formitem = string.Format(formdataTemplate, "headline", "Sunset");
            byte[] formitembytes = System.Text.Encoding.UTF8.GetBytes(formitem);
            memStream.Write(formitembytes, 0, formitembytes.Length);

            memStream.Write(boundarybytes, 0, boundarybytes.Length);


            httpWebRequest2.ContentLength = memStream.Length;

            Stream requestStream = httpWebRequest2.GetRequestStream();

            memStream.Position = 0;
            byte[] tempBuffer = new byte[memStream.Length];
            memStream.Read(tempBuffer, 0, tempBuffer.Length);
            memStream.Close();
            requestStream.Write(tempBuffer, 0, tempBuffer.Length);
            requestStream.Close();


            WebResponse webResponse2 = httpWebRequest2.GetResponse();

            Stream stream2 = webResponse2.GetResponseStream();
            StreamReader reader2 = new StreamReader(stream2);


            Console.WriteLine(reader2.ReadToEnd());

            webResponse2.Close();
            httpWebRequest2 = null;
            webResponse2 = null;

            return "";
        }

        public static string Test(string URL, NameValueCollection nvc, string FileName)
        {
            string resultString = string.Empty;

            FileStream st = new FileStream(FileName, FileMode.Open);
            
            byte[] Tem = new byte[st.Length];

            st.Read(Tem, 0, (int)st.Length);
            st.Close();

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(URL);
            //'--set the standard header information
            req.ProtocolVersion = HttpVersion.Version11;

            req.Method = "POST";
            req.Accept = "*/*";
            req.UserAgent = "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.1; .NET CLR 1.0.3705)";
            //req.ContentType = "application/x-www-form-urlencoded";
            req.ContentType = "multipart/form-data; boundary=23xx1211";
            //'req.AllowAutoRedirect = False
            req.ContentLength = Tem.Length;
            //'--set additional header information
            foreach (string key in nvc.Keys)
            {
                //req.Headers.Add("id", "123456789")
                //req.Headers.Add("merchant_pin", "987654321")
                req.Headers.Add(key, nvc[key]);
            }
            
            //' Perform the request
            Stream requestStream = req.GetRequestStream();
            requestStream.Write(Tem, 0, Tem.Length);
            requestStream.Close();


            //'read in the page
            HttpWebResponse res = (HttpWebResponse)req.GetResponse();
            if(req.HaveResponse)
            {
                StreamReader sr = new StreamReader(res.GetResponseStream());
                resultString = sr.ReadToEnd();
                sr.Close();
            }
            res.Close();

            return resultString;
        }
    }
}
