using System;
using System.Linq;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace HttpLibrary
{
    public class HttpMessage
    {
        public NameValueCollection Headers { get; private set; }
        public Stream Stream { get; private set; }

        public HttpMessage(Stream stream)
        {
            this.Headers = new NameValueCollection();
            this.Stream = stream;
        }

        public virtual long ContentLength
        {
            get { return this.Headers["Content-Length"] == null ? -1 : long.Parse(this.Headers["Content-Length"]); }
            set { this.Headers["Content-Length"] = value.ToString(); }
        }

        public virtual string ContentType
        {
            get { return this.Headers["Content-Type"]; }
            set { this.Headers["Content-Type"] = value; }
        }

        public class Request : HttpMessage
        {
            public string Method { get; private set; }

            public Request(Stream stream, string method) : base(stream)
            {
                Method = method;
            }
        }

        public class Response: HttpMessage
        {
            public HttpStatusCode Status { get; private set; }

            public Response(Stream stream, HttpStatusCode state)
                : base(stream)
            {
                Status = state;
            }
        }
    }

    public abstract class HttpConnection : IDisposable
    {
        public event EventHandler Disposed;

        public CookieContainer CookieContainer { get; set; }

        public HttpMessage.Response Send(string url, HttpMessage.Request request)
        {
            var asyncState = this.BeginSend(url, request, null, null);
            return this.EndSend(asyncState);
        }

        public abstract IAsyncResult BeginSend(string url, HttpMessage.Request request, AsyncCallback callBack, object state);

        public virtual HttpMessage.Response EndSend(IAsyncResult asyncResult)
        {
            var res = (AsyncResultBase)asyncResult;
            res.AsyncWaitHandle.WaitOne(); // wait to complete, if needed
            //res.AsyncWaitHandle.WaitOne(300000, true); //wait 5 min to complete other fail
            if (res.Exception != null)
            {
                throw new ApplicationException("Error occured", res.Exception);
            }
            return res.ResponseMessage;
        }

        public static int CopyStream(Stream input, Stream output)
        {
            const int bufferSize = 4096;
            var buffer = new byte[bufferSize];
            int bytes, total = 0;
            while ((bytes = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, bytes);
                total += bytes;
            }
            return total;
        }

        public void Dispose()
        {
            if (this.Disposed != null)
            {
                this.Disposed(this, EventArgs.Empty);
            }
        }

        protected abstract class AsyncResultBase : IAsyncResult
        {
            private readonly ManualResetEvent handle;

            public bool CompletedSynchronously
            {
                get { throw new NotImplementedException(); }
            }

            public AsyncCallback CallBack { get; private set; }
            public bool IsCompleted { get; private set; }
            public HttpMessage.Response ResponseMessage { get; private set; }
            public HttpMessage.Request RequestMessage { get; private set; }
            public Exception Exception { get; private set; }
            public WaitHandle AsyncWaitHandle { get { return this.handle; } }
            public object AsyncState { get; private set; }

            protected AsyncResultBase(AsyncCallback callBack, object asyncState, HttpMessage.Request requestMessage)
            {
                this.CallBack = callBack;
                AsyncState = asyncState;
                RequestMessage = requestMessage;
                this.handle = new ManualResetEvent(false);
            }

            public void ThrowException(Exception exc)
            {
                this.Exception = exc;
                this.Finish(null);
            }

            protected void Finish(HttpMessage.Response respMsg)
            {
                this.ResponseMessage = respMsg;
                this.IsCompleted = true;
                this.handle.Set();
                if (this.CallBack != null) this.CallBack(this);
            }
        }
    }

    public class HttpWebRequestConnection : HttpConnection
    {
        public override IAsyncResult BeginSend(string url, HttpMessage.Request reqMsg, AsyncCallback callBack, object state)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.CookieContainer = this.CookieContainer;
            foreach (var key in reqMsg.Headers.AllKeys)
            {
                switch (key)
                {
                    case "Content-Type":
                        request.ContentType = reqMsg.Headers[key];
                        break;
                    case "Content-Length":
                        request.ContentLength = long.Parse(reqMsg.Headers[key]);
                        break;
                    case "Referer":
                        request.Referer = reqMsg.Headers[key];
                        break;
                    default:
                        request.Headers.Add(key, reqMsg.Headers[key]);
                        break;
                }
            }
            request.Method = reqMsg.Method;
            var asyncResult = new AsyncResult(state, reqMsg, request, callBack);
            if (request.Method == "GET")
            {
                request.BeginGetResponse(this.OnGetResponse, asyncResult);
            }
            else
            {
                request.BeginGetRequestStream(this.OnGetRequestStream, asyncResult);
            }
            return asyncResult;
        }

        private void OnGetRequestStream(IAsyncResult ar)
        {
            var res = (AsyncResult) ar.AsyncState;
            try
            {
                var outStream = res.Request.EndGetRequestStream(ar);
                CopyStream(res.RequestMessage.Stream, outStream);
                res.Request.BeginGetResponse(this.OnGetResponse, res);
            }
            catch (Exception exc)
            {
                res.ThrowException(exc);
            }
        }

        private void OnGetResponse(IAsyncResult ar)
        {
            var res = (AsyncResult)ar.AsyncState;
            try
            {
                var response = (HttpWebResponse)res.Request.EndGetResponse(ar);
                this.Disposed += (s, e) => response.Close(); // stop downloading on connection dispose
                res.OnSendCompleted(response);
            }
            catch (Exception exc)
            {
                res.ThrowException(exc);
            }
        }

        private class AsyncResult : AsyncResultBase
        {
            public WebRequest Request { get; private set; }

            public AsyncResult(object asyncState, HttpMessage.Request reqMsg, WebRequest webRequest, AsyncCallback callBack)
                : base(callBack, asyncState, reqMsg)
            {
                this.Request = webRequest;
            }

            public void OnSendCompleted(HttpWebResponse response)
            {
                var inStream = response.GetResponseStream();
                var respMsg = new HttpMessage.Response(inStream, response.StatusCode);
                foreach (var key in response.Headers.AllKeys)
                {
                    respMsg.Headers.Add(key, response.Headers[key]);
                }
                this.Finish(respMsg);
            }
        }

    }

    public class HttpSocketConnection : HttpConnection
    {
        private readonly byte[] dataToSend = new byte[1024];
        public NetworkStream NetworkStream { get; private set; }

        public override IAsyncResult BeginSend(string url, HttpMessage.Request request, AsyncCallback callBack, object state)
        {
            var res = new AsyncResult(callBack, state, request, url);
            var uri = res.Uri;
            res.Client.BeginConnect(uri.DnsSafeHost, uri.IsDefaultPort ? 80 : uri.Port, this.OnClientConnect, res);
            return res;
        }

        private void OnClientConnect(IAsyncResult ar)
        {
            var res = (AsyncResult) ar.AsyncState;
            try
            {
                var client = res.Client;
                client.EndConnect(ar);
                client.SendBufferSize = this.dataToSend.Length*4;
                client.ReceiveBufferSize = client.SendBufferSize;
                this.NetworkStream = client.GetStream();
                this.Disposed += (s, e) => this.NetworkStream.Dispose();
                this.StartSending(res);

            }
            catch (Exception exc)
            {
                res.ThrowException(exc);
            }
        }

        private void StartSending(AsyncResult res)
        {
            if (this.CookieContainer != null)
            {
                res.RequestMessage.Headers["Cookie"] = this.CookieContainer.GetCookieHeader(res.Uri);
            }
            var wr = new StringWriter();
            wr.WriteLine("{0} {1} HTTP/1.1", res.RequestMessage.Method, res.Uri.PathAndQuery);
            if (res.Uri.HostNameType == UriHostNameType.Dns)
            {
                wr.WriteLine("Host: {0}", res.Uri.Host);
            }
            foreach (var key in res.RequestMessage.Headers.AllKeys)
            {
                wr.WriteLine("{0}: {1}", key, res.RequestMessage.Headers[key]);
            }
            wr.WriteLine(); // end header section
            var hdrData = Encoding.UTF8.GetBytes(wr.ToString());
            this.NetworkStream.Write(hdrData, 0, hdrData.Length);
            this.SendData(res);
        }

        private void SendData(AsyncResult res)
        {
            var bytes = res.RequestMessage.Stream != null
                            ? res.RequestMessage.Stream.Read(this.dataToSend, 0, this.dataToSend.Length)
                            : 0; // no bytes to send
            if (bytes > 0)
            {
                // there are still data to send
                this.NetworkStream.BeginWrite(this.dataToSend, 0, bytes, this.OnDataSent, res);
            }
            else
            {
                // all data has been sent
                this.NetworkStream.Flush();
                this.OnSendCompleted(res);
            }
        }

        private void OnDataSent(IAsyncResult ar)
        {
            var res = (AsyncResult)ar.AsyncState;
            try
            {
                this.NetworkStream.EndWrite(ar);
                this.SendData(res); // send next data
            }
            catch (Exception exc)
            {
                res.ThrowException(exc);
            }
        }

        private void OnSendCompleted(AsyncResult res)
        {
            var stream = this.NetworkStream;

            /*var buff = new byte[4096];
            var bytes = 0;
            do
            {
                bytes += stream.Read(buff, bytes, buff.Length - bytes);
            } while (bytes < buff.Length);
            File.WriteAllBytes("c:\\response.dat", buff);*/

            var httpStateLine = ReadLine(stream);
            var stateMatch = Regex.Match(httpStateLine, @"^HTTP/\d\.\d (?<code>\d+) .+$");
            var stateCode = (HttpStatusCode)int.Parse(stateMatch.Groups["code"].Value);
            var respMsg = new HttpMessage.Response(stream, stateCode);
            string hdr;
            while (!string.IsNullOrEmpty(hdr = ReadLine(stream)))
            {
                var delimiterPos = hdr.IndexOf(": ");
                respMsg.Headers.Add(hdr.Substring(0, delimiterPos), hdr.Substring(delimiterPos + 1));
            }
            switch (stateCode)
            {
                case HttpStatusCode.Continue:
                    // continue reading response
                    this.OnSendCompleted(res); // read next http response
                    break;
                case HttpStatusCode.Found:
                    // temporary redirect
                    res.Client.Close();
                    stream.Dispose();
                    var newUrl = respMsg.Headers["Location"];
                    if (res.RequestMessage.Stream != null)
                    {
                        // need to send data again, move stream to the beginning
                        res.RequestMessage.Stream.Position = res.RequestStreamStart;
                    }
                    this.BeginSend(newUrl, res.RequestMessage, res.CallBack, res.AsyncState);
                    break;
                case HttpStatusCode.OK:
                case HttpStatusCode.PartialContent:
                    var cook = respMsg.Headers["Set-Cookie"];
                    if (this.CookieContainer != null && !string.IsNullOrEmpty(cook))
                    {
                        this.CookieContainer.SetCookies(res.Uri, cook);
                    }
                    res.OnComplete(respMsg);
                    break;
                default:
                    res.ThrowException(new ApplicationException("Server returned: HTTP " + stateCode));
                    break;
            }
        }

        private static string ReadLine(Stream stream)
        {
            var findSeq = Encoding.ASCII.GetBytes("\r\n"); // expected end of the line
            var buff = new MemoryStream();
            int ch, pos = 0;
            while ((ch = stream.ReadByte()) >= 0)
            {
                buff.WriteByte((byte)ch);
                if (findSeq[pos] == ch)
                {
                    pos++;
                    if (pos >= findSeq.Length)
                    {
                        return Encoding.UTF8.GetString(buff.ToArray(), 0, (int)buff.Length - findSeq.Length);
                    }
                }
                else
                {
                    pos = 0;
                }
            }
            return Encoding.UTF8.GetString(buff.ToArray());
        }

        private class AsyncResult : AsyncResultBase
        {
            public TcpClient Client { get; private set; }
            public Uri Uri { get; set; }
            public long RequestStreamStart { get; private set; }

            public AsyncResult(AsyncCallback callBack, object asyncState, HttpMessage.Request requestMessage, string url)
                : base(callBack, asyncState, requestMessage)
            {
                this.Client = new TcpClient();
                this.Uri = new Uri(url);
                this.RequestStreamStart = requestMessage.Stream != null
                                              ? requestMessage.Stream.Position
                                              : 0;
            }

            public void OnComplete(HttpMessage.Response res)
            {
                this.Finish(res);
            }
        }
    }

    public class WatchedStream : Stream
    {
        private long position;

        public Stream InnerStream { get; private set; }
        public event EventHandler<WatchedStreamTransferArgs> BeforeRead;
        public event EventHandler<WatchedStreamTransferArgs> BeforeWrite;

        public WatchedStream(Stream innerStream)
        {
            InnerStream = innerStream;
            this.position = innerStream.CanSeek ? innerStream.Position : 0;
        }

        public override void Flush()
        {
            this.InnerStream.Flush();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return this.InnerStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            this.InnerStream.SetLength(value);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            this.OnBeforeRead(count);
            var bytes = this.InnerStream.Read(buffer, offset, count);
            this.position += bytes;
            return bytes;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            this.OnBeforeWrite(count);
            this.InnerStream.Write(buffer, offset, count);
            this.position += count;
        }

        public override bool CanRead
        {
            get { return this.InnerStream.CanRead; }
        }

        public override bool CanSeek
        {
            get { return this.InnerStream.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return this.InnerStream.CanWrite; }
        }

        public override long Length
        {
            get { return this.InnerStream.Length; }
        }

        public override long Position
        {
            get { return this.InnerStream.CanSeek ? this.InnerStream.Position : this.position; }
            set { this.InnerStream.Position = value; }
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            this.OnBeforeRead(count);
            return this.InnerStream.BeginRead(buffer, offset, count, callback, state);
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            this.OnBeforeWrite(count);
            this.position += count; // TODO: should be done in EndWrite
            return this.InnerStream.BeginWrite(buffer, offset, count, callback, state);
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            var bytes = this.InnerStream.EndRead(asyncResult);
            this.position += bytes;
            return bytes;
        }

        public override void EndWrite(IAsyncResult asyncResult)
        {
            this.InnerStream.EndWrite(asyncResult);
        }

        protected void OnBeforeRead(int bytes)
        {
            if (this.BeforeRead != null)
            {
                this.BeforeRead(this, new WatchedStreamTransferArgs(bytes, this.Position));
            }
        }

        protected void OnBeforeWrite(int bytes)
        {
            if (this.BeforeWrite != null)
            {
                this.BeforeWrite(this, new WatchedStreamTransferArgs(bytes, this.Position));
            }
        }

        protected override void Dispose(bool disposing)
        {
            this.InnerStream.Dispose();
            base.Dispose(disposing);
        }
    }

    public class BandwidthControlledStream : WatchedStream
    {
        public BandWidthManager ReadSpeed { get; private set; }
        public BandWidthManager WriteSpeed { get; private set; }

        public BandwidthControlledStream(Stream innerStream) : base(innerStream)
        {
            this.ReadSpeed = new BandWidthManager(0);
            this.WriteSpeed = new BandWidthManager(0);
            this.BeforeRead += (s, e) => this.ReadSpeed.DemandNextData(e.Bytes);
            this.BeforeWrite += (s, e) => this.WriteSpeed.DemandNextData(e.Bytes);
        }
    }

    public class BandWidthManager
    {
        readonly object dataSync = new object();
        private DateTime nextCheckpoint;
        private int dataDemanded;
        private int dataLimit;
        private int bytesPerSecond;

        public double CheckpointSeconds { get; set; }

        public BandWidthManager(int bytesPerSecond)
        {
            BytesPerSecond = bytesPerSecond;
            this.CheckpointSeconds = 0.1;
            nextCheckpoint = DateTime.Now;
            this.dataLimit = int.MaxValue;
        }

        public int BytesPerSecond
        {
            get { return bytesPerSecond; }
            set
            {
                lock (this.dataSync)
                {
                    bytesPerSecond = value;
                    this.dataDemanded = 0;
                    this.dataLimit = this.BytesPerSecond == 0
                                         ? int.MaxValue
                                         : (int) (this.BytesPerSecond*this.CheckpointSeconds);
                }
            }
        }

        public void DemandNextData(int bytes)
        {
            var now = DateTime.Now;
            TimeSpan waitTime = TimeSpan.Zero;
            lock (this.dataSync)
            {
                if (this.dataDemanded > dataLimit)
                {
                    // trasferred data exceeds data limit, wait to next checkpoint
                    var exceedRatio = ((double) this.dataDemanded)/this.dataLimit;
                    var bestDuration = TimeSpan.FromSeconds(this.CheckpointSeconds*exceedRatio);
                    var realDuration = TimeSpan.FromSeconds(this.CheckpointSeconds) - (this.nextCheckpoint - now);
                    waitTime = bestDuration - realDuration;
                    if (waitTime > TimeSpan.Zero)
                    {
                        this.dataDemanded = 0;
                    }
                    else // data deficit
                    {
                        this.dataDemanded = (int) -(exceedRatio - 1)*this.dataLimit;
                    }
                    this.nextCheckpoint = now.Add(waitTime).AddSeconds(this.CheckpointSeconds);
                }
                this.dataDemanded += bytes;
            }
            if (waitTime > TimeSpan.Zero)
            {
                Trace.WriteLine(string.Format("Waiting {0:0} ms", waitTime.TotalMilliseconds));
                Thread.Sleep(waitTime);
            }
        }
    }

    public class WatchedStreamTransferArgs : EventArgs
    {
        public long Position { get; private set; }
        public int Bytes { get; private set; }

        public WatchedStreamTransferArgs(int bytes, long pos)
        {
            Bytes = bytes;
            Position = pos;
        }
    }


}
