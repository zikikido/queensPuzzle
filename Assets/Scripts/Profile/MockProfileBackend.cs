using System.Threading.Tasks;

namespace qp {

    /// <summary>Local stand-in for the profile server until the real one exists.</summary>
    public class MockProfileBackend : IProfileBackend {

        readonly string _playerId;   // who is asking — like the header/token a real backend sends

        public MockProfileBackend(string playerId) => _playerId = playerId;

        public Task Push(PlayerProfile profile) => throw new System.NotImplementedException();
    }
}
