// 32feet.NET - Personal Area Networking for .NET
//
// InTheHand.Net.Sockets.LinuxSocket (Linux)
// 
// Copyright (c) 2023-24 In The Hand Ltd, All rights reserved.
// This source code is licensed under the MIT License

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Tmds.DBus;

namespace InTheHand.Net.Sockets {
    /// <summary>
    /// Required because .NET Core on Linux only supports a subset of AddressFamily values,
    /// so it was necessary to build a Socket class from the native APIs.
    /// </summary>
    /// <remarks>To use the Socket on Linux - e.g. that received from BluetoothClient.Client it is necessary to cast to LinuxSocket as it replaces the Socket properties and methods and the base type is a non-functional IPv4 Socket.</remarks>
    public class LinuxSocket : Socket {
        private int _socket = 0;
        private Socket _listener;
        public CloseSafeHandle? _safeHandleRef;

        /// <summary>
        /// Creates a new instance of LinuxSocket.
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">Called on a non-Linux OS.</exception>
        public LinuxSocket() : base(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Unspecified) {
            if (Environment.OSVersion.Platform != PlatformID.Unix)
                throw new PlatformNotSupportedException("Linux library used on non-Linux OS.");

            // AF_BLUETOOTH, Type_Stream, Protocol_Rfcomm
            _socket = NativeMethods.socket((AddressFamily)BluetoothEndPoint.AddressFamilyBlueZ, SocketType.Stream, BluetoothProtocolType.RFComm);
        }

        internal LinuxSocket(int socket) : base(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Unspecified) {
            _socket = socket;
        }

        private void ThrowIfSocketClosed() {
            if (_socket == 0)
                throw new ObjectDisposedException(nameof(LinuxSocket));
        }

        private static void ThrowOnSocketError(int result, bool throwOnDisconnected) {
            if (result == -1) {
                var socketError = Marshal.GetLastSystemError();

                Debug.WriteLine($"Socket Error: {socketError:X8}");
                if (!throwOnDisconnected) {
                    switch (result) {
                        case 10057:
                        case 10060:
                            return;
                    }
                }

                if (socketError != 0)
                    throw new SocketException(socketError);
            }
        }

        /// <inheritdoc/>
        public new LinuxSocket Accept() {
            ThrowIfSocketClosed();

            var newSocket = NativeMethods.accept(_socket, null, 0);

            ThrowOnSocketError(newSocket, true);

            return new LinuxSocket(newSocket);
        }

        /// <inheritdoc/>
        public new int Available {
            get {
                var result = NativeMethods.ioctl(_socket, NativeMethods.FIONREAD, out var len);
                ThrowOnSocketError(result, true);

                return len;
            }
        }

        /// <inheritdoc/>
        public new void Bind(EndPoint localEP) {
            ThrowIfSocketClosed();

            if (localEP == null)
                throw new ArgumentNullException(nameof(localEP));

            var sockAddr = localEP.Serialize();
            var raw = sockAddr.ToByteArray();

            var result = NativeMethods.bind(_socket, raw, sockAddr.Size);

            ThrowOnSocketError(result, true);
        }

        /// <inheritdoc/>
        public new bool IsBound {
            get {
                var ep = LocalEndPointRaw;
                if (ep != null) {
                    // check if local endpoint port is not zero
                    if (BitConverter.ToInt32(ep, 8) != 0)
                        return true;
                }

                return false;
            }
        }

        /// <inheritdoc/>
        public new bool Connected {
            get {
                if (_socket == 0)
                    return false;

                return RemoteEndPoint != null;
            }
        }

        /// <inheritdoc/>
        public new void Close() {
            if (_socket != 0) {
                var fd = _socket;
                _socket = 0;
                _safeHandleRef = null; // release reference, but NativeMethods.close() owns actual close
                var result = NativeMethods.close(fd);
                base.Close();
                ThrowOnSocketError(result, false);
            }
        }

        /// <inheritdoc/>
        public new void Connect(EndPoint remoteEP) {
            ThrowIfSocketClosed();

            if (remoteEP == null)
                throw new ArgumentNullException(nameof(remoteEP));

            var sa = remoteEP.Serialize().ToByteArray();

            for (int i = 0; i < 10; i++) {
                Debug.Write(sa[i].ToString("X2"));
            }
            Debug.WriteLine("");

            var result = NativeMethods.connect(_socket, sa, 10);

            int error = Marshal.GetLastPInvokeError();
            Console.WriteLine($"Entry failed with errno: {error}");

            // To get a human-readable string:
            string errorMessage = Marshal.GetPInvokeErrorMessage(error);
            Console.WriteLine($"Description: {errorMessage}");
            ThrowOnSocketError(result, true);
            using (Socket lstnr = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Unspecified)) {
                lstnr.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                lstnr.Listen(1);
                EndPoint svrEp = lstnr.LocalEndPoint;
                base.Connect(svrEp);
                _listener = lstnr.Accept();
            }
        }

        /// <inheritdoc/>
        public new int Receive(byte[] buffer) {
            return RawReceive(buffer, buffer.Length, SocketFlags.None);
        }

        /// <inheritdoc/>
        public new int Receive(byte[] buffer, SocketFlags socketFlags) {
            return RawReceive(buffer, buffer.Length, socketFlags);
        }

        private int RawReceive(byte[] buffer, int size, SocketFlags socketFlags) {
            ThrowIfSocketClosed();

            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (size > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(size));

            int result;
            while (true) {
                result = NativeMethods.recv(_socket, buffer, size, (int)socketFlags);

                if (result == -1 && Marshal.GetLastWin32Error() == 11) // EAGAIN
                {
                    var pollFd = new PollFd { fd = _socket, events = POLLIN };
                    NativeMethods.poll(ref pollFd, 1, -1); // block until readable
                    continue;
                }
                break;
            }

            ThrowOnSocketError(result, true);

            return result;
        }

        /// <inheritdoc/>
        public new int Receive(byte[] buffer, int size, SocketFlags socketFlags) {
            return RawReceive(buffer, size, socketFlags);
        }

        /// <inheritdoc/>
        public new int Receive(byte[] buffer, int offset, int size, SocketFlags socketFlags) {
            var newBuffer = new byte[size];
            var bytesReceived = RawReceive(newBuffer, size, socketFlags);
            if (bytesReceived > 0) {
                newBuffer.CopyTo(buffer, offset);
            }

            return bytesReceived;
        }

        /// <inheritdoc/>
        public new int Receive(byte[] buffer, int offset, int size, SocketFlags socketFlags, out SocketError errorCode) {
            ThrowIfSocketClosed();

            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (offset + size > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(size));

            var newBuffer = new byte[size];

            int bytesReceived;
            try {
                bytesReceived = RawReceive(newBuffer, size, socketFlags);
                errorCode = SocketError.Success;
            }
            catch (SocketException ex) {
                errorCode = (SocketError)ex.ErrorCode;
                return 0;
            }

            if (bytesReceived > 0)
                newBuffer.CopyTo(buffer, offset);

            return bytesReceived;
        }

        /// <inheritdoc/>
        public new void Listen(int backlog) {
            var result = NativeMethods.listen(_socket, backlog);
            ThrowOnSocketError(result, true);
        }

        /// <inheritdoc/>
        public new EndPoint LocalEndPoint => new BluetoothEndPoint(LocalEndPointRaw);

        internal byte[] LocalEndPointRaw {
            get {
                var addr = new byte[30];
                var len = addr.Length;
                var result = NativeMethods.getsockname(_socket, addr, ref len);
                ThrowOnSocketError(result, false);

                return addr;
            }
        }

        /// <inheritdoc/>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Matching Socket API")]
        public new bool Poll(int microSeconds, SelectMode mode) {
            var fileDescriptorSet = new int[2] { 1, _socket };
            var result = NativeMethods.select(0, mode == SelectMode.SelectRead ? fileDescriptorSet : null, mode == SelectMode.SelectWrite ? fileDescriptorSet : null, mode == SelectMode.SelectError ? fileDescriptorSet : null, IntPtr.Zero);

            if (result == -1) {
                ThrowOnSocketError(result, true);
            }

            if (fileDescriptorSet[0] == 0) {
                return false;
            }

            return fileDescriptorSet[1] == _socket;
        }

        /// <inheritdoc/>
        public new EndPoint RemoteEndPoint {
            get {
                var addr = new byte[30];
                var len = addr.Length;
                var result = NativeMethods.getpeername(_socket, addr, ref len);
                Debug.WriteLine($"getpeername: {result}");
                if (result == 0)
                    return new BluetoothEndPoint(addr);

                ThrowOnSocketError(result, false);

                return null;
            }
        }

        /// <inheritdoc/>
        public new int Send(byte[] buffer) {
            return RawSend(buffer, 0, buffer.Length, 0);
        }

        /// <inheritdoc/>
        public new int Send(byte[] buffer, SocketFlags socketFlags) {
            return RawSend(buffer, 0, buffer.Length, socketFlags);
        }

        /// <inheritdoc/>
        public new int Send(byte[] buffer, int size, SocketFlags socketFlags) {
            return RawSend(buffer, 0, size, socketFlags);
        }

        /// <inheritdoc/>
        public new int Send(byte[] buffer, int offset, int size, SocketFlags socketFlags) {
            return RawSend(buffer, offset, size, socketFlags);
        }

        private int RawSend(byte[] buffer, int offset, int size, SocketFlags socketFlags) {
            ThrowIfSocketClosed();

            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset + size > buffer.Length) throw new ArgumentOutOfRangeException(nameof(size));

            int totalSent = 0;

            while (totalSent < size) {
                int remaining = size - totalSent;
                byte[] chunk = new byte[remaining];
                Array.Copy(buffer, offset + totalSent, chunk, 0, remaining);

                int result = NativeMethods.send(_socket, chunk, remaining, (int)socketFlags | 0x4000);

                if (result > 0) {
                    totalSent += result;
                }
                else if (result == -1) {
                    int errorCode = Marshal.GetLastPInvokeError();

                    // Handle socket busy
                    if (errorCode == 11) {
                        var pollFd = new PollFd { fd = _socket, events = POLLOUT };
                        int pollResult = NativeMethods.poll(ref pollFd, 1, -1);

                        if (pollResult == -1)
                            throw new SocketException(Marshal.GetLastPInvokeError());

                        if ((pollFd.revents & (POLLERR | POLLHUP)) != 0) {
                            // Get the specific socket error to avoid the "Success" exception
                            int sockErr;
                            int optlen = Marshal.SizeOf(typeof(int));
                            NativeMethods.getsockopt(_socket, 1, 4, out sockErr, ref optlen);
                            throw new SocketException(sockErr != 0 ? sockErr : errorCode);
                        }

                        // Poll finished successfully, try the loop again
                        continue;
                    }

                    // A genuine error occurred
                    throw new SocketException(errorCode);
                }
                else if (result == 0) {
                    // The peer closed the connection
                    break;
                }
            }

            return totalSent;
        }

        /// <inheritdoc/>
        public new void SetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, bool optionValue) {
            var result = NativeMethods.setsockopt(_socket, (int)optionLevel, (int)optionName, BitConverter.GetBytes(Convert.ToInt32(optionValue)), Marshal.SizeOf(typeof(int)));
            ThrowOnSocketError(result, true);
        }

        /// <inheritdoc/>
        public new void SetSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, int optionValue) {
            var result = NativeMethods.setsockopt(_socket, (int)optionLevel, (int)optionName, BitConverter.GetBytes(optionValue), Marshal.SizeOf(typeof(int)));
            ThrowOnSocketError(result, true);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing) {
            Close();
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PollFd {
            public int fd;
            public short events;
            public short revents;
        }

        private const short POLLIN = 0x0001;
        private const short POLLOUT = 0x0004;
        private const short POLLERR = 0x0008;
        private const short POLLHUP = 0x0010;
        private const int SOL_SOCKET = 1;
        private const int SO_ERROR = 4;

        private static class NativeMethods {
            private const string libc = "libc";
            internal const int FIONREAD = 0x4004667F;

#pragma warning disable IDE1006 // Naming Styles - these are API function names
            [DllImport(libc)]
            internal static extern int socket(AddressFamily af, SocketType type, ProtocolType protocol);

            [DllImport(libc)]
            internal static extern int close(int fd);

            [DllImport(libc, SetLastError = true)]
            internal static extern int connect(int s, [MarshalAs(UnmanagedType.LPArray)] byte[] name, int namelen);

            [DllImport(libc, SetLastError = true)]
            internal static extern int recv(int s, byte[] buf, int len, int flags);

            [DllImport(libc, SetLastError = true)]
            internal static extern int send(int s, byte[] buf, int len, int flags);

            [DllImport(libc)]
            internal static extern int bind(int s, byte[] name, int namelen);

            [DllImport(libc)]
            internal static extern int listen(int s, int backlog);

            [DllImport(libc)]
            internal static extern int accept(int s, byte[] addr, int addrlen);

            [DllImport(libc)]
            internal static extern int getsockname(int s, byte[] addr, ref int addrlen);

            [DllImport(libc)]
            internal static extern int getpeername(int s, byte[] addr, ref int addrlen);

            [DllImport(libc)]
            internal static extern int ioctl(int s, int cmd, out int argp);

            [DllImport(libc)]
            internal static extern int select(int nfds, int[] readfds, int[] writefds, int[] exceptfds, IntPtr timeout);

            [DllImport(libc)]
            internal static extern int setsockopt(int s, int level, int optname, byte[] optval, int optlen);

            [DllImport("libc", SetLastError = true)]
            internal static extern int poll(ref PollFd fds, uint nfds, int timeout);

            [DllImport("libc", SetLastError = true)]
            internal static extern int getsockopt(
                int s,
                int level,
                int optname,
                out int optval,
                ref int optlen);

            [DllImport("libc", SetLastError = true)]
            internal static extern int getsockopt(
                int s,
                int level,
                int optname,
                [MarshalAs(UnmanagedType.LPArray)] byte[] optval,
                ref int optlen);
#pragma warning restore IDE1006 // Naming Styles

        }
    }
}
