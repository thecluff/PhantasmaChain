﻿using Phantasma.Blockchain;
using Phantasma.Blockchain.Contracts;
using Phantasma.Blockchain.Contracts.Native;
using Phantasma.Blockchain.Tokens;
using Phantasma.Cryptography;
using Phantasma.VM.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Phantasma.Tests
{
    public class SideChainPendingTransaction
    {
        public Address address;
        public Hash hash;
        public Chain sourceChain;
        public Chain destChain;
        public Block block;
    }

    // TODO this should be moved to a better place, refactored or even just deleted if no longer useful
    public class ChainSimulator
    {
        public Nexus Nexus { get; private set; }

        private System.Random _rnd;
        private List<KeyPair> _keys = new List<KeyPair>();
        private KeyPair _owner;

        private DateTime _currentTime;

        private Chain bankChain;
        private Chain accountChain;

        private static readonly string[] accountNames = {
            "aberration", "absence", "aceman", "acid", "alakazam", "alien", "alpha", "angel", "angler", "anomaly", "answer", "antsharer", "aqua", "archangel",
            "aspect", "atom", "avatar", "azure", "behemoth", "beta", "bishop", "bite", "blade", "blank", "blazer", "bliss", "boggle", "bolt",
            "bullet", "bullseye", "burn", "chaos", "charade", "charm", "chase", "chief", "chimera", "chronicle", "cipher", "claw", "cloud", "combo",
            "comet", "complex", "conjurer", "cowboy", "craze", "crotchet", "crow", "crypto", "cryptonic", "curse", "dagger", "dante", "daydream",
            "dexter", "diablo", "doctor", "doppelganger", "drake", "dread", "ecstasy", "enigma", "epitome", "essence", "eternity", "face",
            "fetish", "fiend", "flash", "fragment", "freak", "fury", "ghoul", "gloom", "gluttony", "grace", "griffin", "grim",
            "whiz", "wolf", "wrath", "zero", "zigzag", "zion"
        };


        private List<SideChainPendingTransaction> _pendingTxs = new List<SideChainPendingTransaction>();

        public ChainSimulator(KeyPair ownerKey, int seed)
        {
            _owner = ownerKey;
            this.Nexus = new Nexus(_owner);

            this.bankChain = Nexus.FindChainByKind(ContractKind.Bank);
            this.accountChain = Nexus.FindChainByKind(ContractKind.Account);

            var miner = KeyPair.Generate();
            var third = KeyPair.Generate();

            _rnd = new System.Random(seed);
            _keys.Add(_owner);

            _currentTime = new DateTime(2018, 8, 26);
        }

        private List<Transaction> transactions = new List<Transaction>();

        // there are more elegant ways of doing this...
        private Dictionary<Hash, Chain> txChainMap = new Dictionary<Hash, Chain>();
        private Dictionary<Hash, Transaction>  txHashMap = new Dictionary<Hash, Transaction>();

        private HashSet<Address> pendingNames = new HashSet<Address>();

        private bool blockOpen = false;

        public void BeginBlock()
        {
            if (blockOpen)
            {
                throw new Exception("Simulator block not terminated");
            }

            transactions.Clear();
            txChainMap.Clear();
            txHashMap.Clear();
            pendingNames.Clear();

            blockOpen = true;
        }

        public void EndBlock()
        {
            if (!blockOpen)
            {
                throw new Exception("Simulator block not open");
            }

            var chains = txChainMap.Values.Distinct();

            foreach (var chain in chains)
            {
                var hashes = txChainMap.Where((p) => p.Value == chain).Select(x => x.Key);
                if (hashes.Any())
                {
                    var txs = new List<Transaction>();
                    foreach (var hash in hashes)
                    {
                        txs.Add(txHashMap[hash]);
                    }

                    var block = new Block(chain, _owner.Address, _currentTime, txs, chain.LastBlock);
                    if (block.Chain.AddBlock(block))
                    {
                        _currentTime += TimeSpan.FromMinutes(45);

                        foreach (var entry in _pendingTxs)
                        {
                            if (txHashMap.ContainsKey(entry.hash))
                            {
                                entry.block = block;
                            }
                        }
                    }
                    else
                    {
                        throw new Exception($"add block in {chain.Name} failed");
                    }
                }
            }

            blockOpen = false;
        }

        public void GenerateRandomBlock()
        {
            BeginBlock();

            int transferCount = 1 + _rnd.Next() % 10;
            while (transactions.Count < transferCount)
            {
                var source = _keys[_rnd.Next() % _keys.Count];

                var chain = Nexus.RootChain;
                Token token;

                switch (_rnd.Next() % 4)
                {
                    case 1: token = Nexus.StableToken; break;
                    default: token = Nexus.NativeToken; break;
                }


                switch (_rnd.Next() % 7)
                {
                    // side-chain send
                    case 1:
                        {
                            var balance = chain.GetTokenBalance(token, source.Address);

                            var chainList = Nexus.Chains.ToArray();
                            var targetChain = chainList[_rnd.Next() % chainList.Length];

                            var total = balance / 10;
                            if (total > 0 && targetChain != chain)
                            {
                                var script = ScriptUtils.CallContractScript(chain, "SendTokens", targetChain.Address, source.Address, source.Address, token.Symbol, total);
                                var tx = new Transaction(script, 0, 0);
                                tx.Sign(source);

                                txChainMap[tx.Hash] = chain;
                                txHashMap[tx.Hash] = tx;
                                transactions.Add(tx);

                                var pending = new SideChainPendingTransaction()
                                {
                                    address = source.Address,
                                    sourceChain = chain,
                                    destChain = targetChain,
                                    hash = tx.Hash,
                                    block = null,
                                };
                                _pendingTxs.Add(pending);
                            }

                            break;
                        }

                    // side-chain receive
                    case 2:
                        {
                            SideChainPendingTransaction targetTransaction = null;
                            foreach (var entry in _pendingTxs)
                            {
                                if (entry.address == source.Address)
                                {
                                    chain = entry.sourceChain;
                                    targetTransaction = entry;
                                    break;
                                }
                            }

                            if (targetTransaction != null && targetTransaction.block != null)
                            {
                                _pendingTxs.RemoveAll(x => x.hash == targetTransaction.hash);

                                chain = targetTransaction.destChain;

                                var script = ScriptUtils.CallContractScript(chain, "ReceiveTokens", targetTransaction.sourceChain.Address, targetTransaction.address, targetTransaction.hash);
                                var tx = new Transaction(script, 0, 0);
                                tx.Sign(source);

                                txChainMap[tx.Hash] = chain;
                                txHashMap[tx.Hash] = tx;
                                transactions.Add(tx);
                            }

                            break;
                        }

                    // stable claim
                    case 3:
                        {
                            chain = bankChain;
                            token = Nexus.NativeToken;

                            var balance = chain.GetTokenBalance(token, source.Address);

                            var total = balance / 10;
                            if (total > 0)
                            {
                                var script = ScriptUtils.CallContractScript(chain, "Claim", source.Address, total);
                                var tx = new Transaction(script, 0, 0);
                                tx.Sign(source);

                                txChainMap[tx.Hash] = chain;
                                txHashMap[tx.Hash] = tx;
                                transactions.Add(tx);
                            }

                            break;
                        }

                    // stable redeem
                    case 4:
                        {
                            chain = bankChain;
                            token = Nexus.StableToken;

                            var balance = chain.GetTokenBalance(token, source.Address);

                            var total = balance / 10;
                            if (total > 0)
                            {
                                var script = ScriptUtils.CallContractScript(chain, "Redeem", source.Address, total);
                                var tx = new Transaction(script, 0, 0);
                                tx.Sign(source);

                                txChainMap[tx.Hash] = chain;
                                txHashMap[tx.Hash] = tx;
                                transactions.Add(tx);
                            }

                            break;
                        }

                    // name register
                    case 5:
                        {
                            chain = accountChain;
                            token = Nexus.NativeToken;

                            var balance = chain.GetTokenBalance(token, source.Address);
                            if (balance >= AcountContract.RegistrationCost && !pendingNames.Contains(source.Address))
                            {
                                var randomName = accountNames[_rnd.Next() % accountNames.Length];

                                switch (_rnd.Next() % 10)
                                {
                                    case 1:
                                    case 2:
                                        randomName += (_rnd.Next() % 10).ToString();
                                        break;

                                    case 3:
                                    case 4:
                                    case 5:
                                        randomName += (10 + _rnd.Next() % 90).ToString();
                                        break;

                                    case 6:
                                        randomName += (100 +_rnd.Next() % 900).ToString();
                                        break;
                                }

                                var lookup = Nexus.LookUpName(randomName);
                                if (lookup == Address.Null)
                                {
                                    var script = ScriptUtils.CallContractScript(chain, "Register", source.Address, randomName);
                                    var tx = new Transaction(script, 0, 0);
                                    tx.Sign(source);

                                    txChainMap[tx.Hash] = chain;
                                    txHashMap[tx.Hash] = tx;
                                    transactions.Add(tx);

                                    pendingNames.Add(source.Address);
                                }
                            }

                            break;
                        }

                    // normal transfer
                    default:
                        {
                            var temp = _rnd.Next() % 5;
                            Address targetAddress;

                            if (_keys.Count < 2 || temp == 0)
                            {
                                var key = KeyPair.Generate();
                                _keys.Add(key);
                                targetAddress = key.Address;
                            }
                            else
                            {
                                targetAddress = _keys[_rnd.Next() % _keys.Count].Address;
                            }

                            if (source.Address != targetAddress)
                            {
                                var balance = chain.GetTokenBalance(token, source.Address);

                                var total = balance / 10;
                                if (total > 0)
                                {
                                    var script = ScriptUtils.CallContractScript(chain, "TransferTokens", source.Address, targetAddress, token.Symbol, total);
                                    var tx = new Transaction(script, 0, 0);
                                    tx.Sign(source);

                                    txChainMap[tx.Hash] = chain;
                                    txHashMap[tx.Hash] = tx;
                                    transactions.Add(tx);
                                }
                            }
                            break;
                        }
                }
            }

            EndBlock();
        }
    }

}