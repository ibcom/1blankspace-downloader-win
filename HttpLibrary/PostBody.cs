using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace HttpLibrary
{
    public abstract class HttpPostBodyBuilder
    {
        public abstract void AddParameter(string name, string value);

        public abstract Stream PrepareData();
        public abstract string GetContentType();

        public class UrlEncoded : HttpPostBodyBuilder
        {
            private readonly MemoryStream buff;
            private readonly StreamWriter sw;

            public UrlEncoded()
            {
                this.buff = new MemoryStream();
                this.sw = new StreamWriter(this.buff, Encoding.ASCII);
                this.sw.AutoFlush = true;
            }

            public override void AddParameter(string name, string value)
            {
                if (this.buff.Length > 0) sw.Write('&');
                sw.Write(name);
                sw.Write('=');
                sw.Write(HttpUtility.UrlEncode(value, Encoding.UTF8));
            }

            public override Stream PrepareData()
            {
                this.buff.Position = 0;
                return this.buff;
            }

            public override string GetContentType()
            {
                return "application/x-www-form-urlencoded; charset=UTF-8";
            }
        }

        public class Multipart : HttpPostBodyBuilder
        {
            private readonly List<Parameter> pars = new List<Parameter>();

            public override void AddParameter(string name, string value)
            {
                this.pars.Add(new Parameter(name, null, null, new MemoryStream(Encoding.UTF8.GetBytes(value))));
            }

            public void AddData(string name, Stream data, string fileName, string contentType)
            {
                this.pars.Add(new Parameter(name, contentType, fileName, data));
            }

            private string GetBoundary()
            {
                return "---------------------------7d4285126106b0"; // should be better....
            }

            public override string GetContentType()
            {
                return "multipart/form-data; boundary=" + this.GetBoundary();
            }

            public override Stream PrepareData()
            {
                var bound = this.GetBoundary();
                var buff = new List<Stream>();
                foreach (var par in this.pars)
                {
                    string fieldHeader;
                    if (par.ContentType != null)
                    {
                        fieldHeader = string.Format(@"--{0}
Content-Disposition: form-data; name=""{1}""; filename=""{2}""
Content-Type: {3}

", bound, par.Name, par.FileName, par.ContentType);
                    }
                    else
                    {
                        fieldHeader = string.Format(@"--{0}
Content-Disposition: form-data; name=""{1}""

", bound, par.Name);
                    }
                    buff.Add(new MemoryStream(Encoding.UTF8.GetBytes(fieldHeader)));
                    buff.Add(par.ParameterData);
                    buff.Add(new MemoryStream(Encoding.UTF8.GetBytes("\r\n")));
                }
                buff.Add(new MemoryStream(Encoding.UTF8.GetBytes(string.Format(@"--{0}--

", bound))));
                return new MergingStream(buff.ToArray());
            }

            private class Parameter
            {
                public string Name { get; private set; }
                public string ContentType { get; private set; }
                public string FileName { get; private set; }
                public Stream ParameterData { get; private set; }

                public Parameter(string name, string contentType, string fileName, Stream parameterData)
                {
                    Name = name;
                    ContentType = contentType;
                    FileName = fileName;
                    ParameterData = parameterData;
                }
            }
        }
    }

    public class MergingStream : Stream
    {
        private readonly Stream[] innerStreams;
        private int currentStream;
        private long currentPosition;
        private readonly long totalLength;

        public MergingStream(Stream[] innerStreams)
        {
            this.innerStreams = innerStreams;
            this.currentStream = 0;
            this.currentPosition = 0;
            this.totalLength = innerStreams.Sum(s => s.Length);
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    this.Position = offset;
                    break;
                case SeekOrigin.Current:
                    this.Position += offset;
                    break;
                case SeekOrigin.End:
                    this.Position = this.Length - offset;
                    break;
            }
            return this.Position;
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesDone = 0;
            var pos = this.currentPosition;
            var index = this.currentStream;
            while (bytesDone < count && index < this.innerStreams.Length)
            {
                var stream = this.innerStreams[index];
                var len = stream.Length;
                var bytesCount = (int)(len - pos);
                var bytesLeft = count - bytesDone;
                if (bytesLeft < bytesCount) bytesCount = bytesLeft;
                stream.Position = pos;
                if (bytesCount > 0)
                {
                    stream.Read(buffer, bytesDone + offset, bytesCount);
                }
                bytesDone += bytesCount;
                pos += bytesCount;
                if (pos >= len)
                {
                    pos = 0; // move to next stream
                    index++;
                }
            }
            this.currentPosition = pos;
            this.currentStream = index;
            return bytesDone;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return true; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override long Length
        {
            get { return this.totalLength; }
        }

        public override long Position
        {
            get { return this.innerStreams.Take(this.currentStream).Sum(s => s.Length) + this.currentPosition; }
            set
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException("value");
                }
                long pos = value;
                int streamIndex = 0;
                long streamLength;
                while (pos > (streamLength = this.innerStreams[streamIndex].Length))
                {
                    pos -= streamLength;
                    streamIndex++;
                    if (streamIndex > this.innerStreams.Length && pos > 0)
                    {
                        throw new ArgumentOutOfRangeException("value");
                    }
                }
                this.currentStream = streamIndex;
                this.currentPosition = pos;
            }
        }
    }

    public class HttpUtility
    {
        // obtained using Reflector

        public static string UrlEncode(string str, Encoding e)
        {
            if (str == null)
            {
                return null;
            }
            return Encoding.ASCII.GetString(UrlEncodeToBytes(str, e));
        }

        public static byte[] UrlEncodeToBytes(string str, Encoding e)
        {
            if (str == null)
            {
                return null;
            }
            byte[] bytes = e.GetBytes(str);
            return UrlEncodeBytesToBytesInternal(bytes, 0, bytes.Length, false);
        }

        private static byte[] UrlEncodeBytesToBytesInternal(byte[] bytes, int offset, int count, bool alwaysCreateReturnValue)
        {
            int num = 0;
            int num2 = 0;
            for (int i = 0; i < count; i++)
            {
                char ch = (char)bytes[offset + i];
                if (ch == ' ')
                {
                    num++;
                }
                else if (!IsSafe(ch))
                {
                    num2++;
                }
            }
            if ((!alwaysCreateReturnValue && (num == 0)) && (num2 == 0))
            {
                return bytes;
            }
            byte[] buffer = new byte[count + (num2 * 2)];
            int num4 = 0;
            for (int j = 0; j < count; j++)
            {
                byte num6 = bytes[offset + j];
                char ch2 = (char)num6;
                if (IsSafe(ch2))
                {
                    buffer[num4++] = num6;
                }
                else if (ch2 == ' ')
                {
                    buffer[num4++] = 0x2b;
                }
                else
                {
                    buffer[num4++] = 0x25;
                    buffer[num4++] = (byte)IntToHex((num6 >> 4) & 15);
                    buffer[num4++] = (byte)IntToHex(num6 & 15);
                }
            }
            return buffer;
        }

        internal static bool IsSafe(char ch)
        {
            if ((((ch >= 'a') && (ch <= 'z')) || ((ch >= 'A') && (ch <= 'Z'))) || ((ch >= '0') && (ch <= '9')))
            {
                return true;
            }
            switch (ch)
            {
                case '\'':
                case '(':
                case ')':
                case '*':
                case '-':
                case '.':
                case '_':
                case '!':
                    return true;
            }
            return false;
        }

        internal static char IntToHex(int n)
        {
            if (n <= 9)
            {
                return (char)(n + 0x30);
            }
            return (char)((n - 10) + 0x61);
        }
    }
}
