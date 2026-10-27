using System;
using System.IO;
using Assets.Scripts.Network.NetCore;
using NUnit.Framework;

namespace ServerCore.Networking.Tests
{
    public sealed class IdentityStoreTests
    {
        private string directory;
        [SetUp] public void Setup() => directory = Path.Combine(Path.GetTempPath(), "ServerCoreNetworkTests", Guid.NewGuid().ToString("N"));
        [TearDown] public void Cleanup() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        [Test]
        public void NewInstancesRecoverClientAndServerIdentity()
        {
            var path = Path.Combine(directory, "client.json");
            var registryPath = Path.Combine(directory, "server.json");
            var first = new FileClientIdentityStore(path).LoadOrCreate();
            var registry = new FileClientRegistry(registryPath);
            registry.Register(first, out var reconnect);
            Assert.That(reconnect, Is.False);
            var second = new FileClientIdentityStore(path).LoadOrCreate();
            Assert.That(second.ClientId, Is.EqualTo(first.ClientId));
            Assert.That(second.ReconnectToken, Is.EqualTo(first.ReconnectToken));
            var restored = new FileClientRegistry(registryPath);
            restored.Register(second, out reconnect);
            Assert.That(reconnect, Is.True);
            Assert.That(restored.KnownClients.Count, Is.EqualTo(1));
            StringAssert.DoesNotContain(first.ReconnectToken, File.ReadAllText(registryPath));
        }

        [Test]
        public void WrongCredentialDoesNotOverwriteKnownClient()
        {
            var registry = new FileClientRegistry(Path.Combine(directory, "server.json"));
            var identity = ClientIdentity.Create();
            registry.Register(identity, out _);
            var forged = ClientIdentity.Create();
            forged.ClientId = identity.ClientId;
            Assert.Throws<UnauthorizedAccessException>(() => registry.Register(forged, out _));
            registry.Register(identity, out var reconnect);
            Assert.That(reconnect, Is.True);
            Assert.That(registry.KnownClients.Count, Is.EqualTo(1));
        }

        [TestCase("client.json")]
        [TestCase("server.json")]
        public void CorruptionIsReportedWithoutReplacingTheFile(string name)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, "{ broken file");
            Assert.Catch(() =>
            {
                if (name == "client.json") new FileClientIdentityStore(path).LoadOrCreate();
                else new FileClientRegistry(path);
            });
            Assert.That(File.ReadAllText(path), Is.EqualTo("{ broken file"));
        }

        [Test]
        public void FailedPersistenceDoesNotRegisterClientInMemory()
        {
            Directory.CreateDirectory(directory);
            var blocker = Path.Combine(directory, "not-a-directory");
            File.WriteAllText(blocker, "keep");
            var registry = new FileClientRegistry(Path.Combine(blocker, "server.json"));
            Assert.Catch<IOException>(() => registry.Register(ClientIdentity.Create(), out _));
            Assert.That(registry.KnownClients.Count, Is.Zero);
            Assert.That(File.ReadAllText(blocker), Is.EqualTo("keep"));
        }
    }
}
