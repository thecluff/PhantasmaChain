﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using Phantasma.Core;
using Phantasma.Cryptography;
using Phantasma.Storage;
using Phantasma.Network.P2P.Messages;
using Phantasma.Cryptography.EdDSA;
using Phantasma.Storage.Utils;

namespace Phantasma.Network.P2P
{
    /// <summary>
    /// Represents a generic network message
    /// </summary>
    public abstract class Message
    {
        public Opcode Opcode { get; private set; }
        public string Host { get; private set; }
        public Address Address { get; private set; }
        public Signature Signature { get; private set; }

        public bool IsSigned => !Address.IsNull && Signature != null;

        public Message(Opcode opcode, Address address, string host) {
            this.Opcode = opcode;
            this.Address = address;
            this.Host = host;
        }

        public void Sign(PhantasmaKeys keyPair)
        {
            Throw.If(keyPair.Address != this.Address, "unexpected keypair");

            var msg = this.ToByteArray(false);

            this.Signature = Ed25519Signature.Generate(keyPair, msg);
        }

        public static Message Unserialize(BinaryReader reader)
        {
            var opcode = (Opcode)reader.ReadByte();
            var address = reader.ReadAddress();
            var host = reader.ReadVarString();

            Message msg;

            switch (opcode)
            {
                case Opcode.REQUEST:
                    {
                        msg = RequestMessage.FromReader(address, host, reader);
                        break;
                    }

                case Opcode.EVENT:
                    {
                        msg = EventMessage.FromReader(address, host, reader);
                        break;
                    }

                case Opcode.LIST:
                    {
                        msg = ListMessage.FromReader(address, host, reader);
                        break;
                    }

                case Opcode.MEMPOOL_Add:
                    {
                        msg = MempoolAddMessage.FromReader(address, host, reader);
                        break;
                    }

                /*                case Opcode.MEMPOOL_List:
                                    {
                                        msg = MempoolGetMessage.FromReader(address, reader);
                                        break;
                                    }

                                case Opcode.CHAIN_List:
                                    {
                                        msg = ChainListMessage.FromReader(address, reader);
                                        break;
                                    }
                                    */

                case Opcode.ERROR:
                    {
                        msg = ErrorMessage.FromReader(address, host, reader);
                        break;
                    }

                default: return null;
            }

            if (address != null)
            {
                msg.Signature = reader.ReadSignature();
            }

            return msg;
        }

        public byte[] ToByteArray(bool withSignature)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    Serialize(writer, withSignature);
                }

                return stream.ToArray();
            }
        }

        public void Serialize(BinaryWriter writer, bool withSignature)
        {
            writer.Write((byte)Opcode);
            writer.WriteAddress(Address);
            writer.WriteVarString(Host);

            OnSerialize(writer);

            if (withSignature)
            {
                Throw.IfNull(Signature, nameof(Signature));

                writer.WriteSignature(Signature);
            }
        }

        protected abstract void OnSerialize(BinaryWriter writer);

        public virtual IEnumerable<string> GetDescription()
        {
            return Enumerable.Empty<string>();
        }
    }
}
