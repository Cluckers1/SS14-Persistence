using Content.Server.Administration.Logs;
using Content.Server.Radio.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared._Persistence14.PersistentIdentifier;
using Content.Shared._Persistence14.Research;
using Content.Shared.Access.Systems;
using Content.Shared.Popups;
using Content.Shared.Research.Components;
using Content.Shared.Research.Systems;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Content.Server.Research.Systems
{
    [UsedImplicitly]
    public sealed partial class ResearchSystem : SharedResearchSystem
    {
        [Dependency] private readonly IAdminLogManager _adminLog = default!;
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly AccessReaderSystem _accessReader = default!;
        [Dependency] private readonly EntityLookupSystem _lookup = default!;
        [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
        [Dependency] private readonly SharedPopupSystem _popup = default!;
        [Dependency] private readonly RadioSystem _radio = default!;
        [Dependency] private readonly StationSystem _station = default!;
        [Dependency] private PersistentIdentifierSystem _pid = default!;

        public override void Initialize()
        {
            base.Initialize();
            InitializeConsole();
            InitializeSource();
            InitializeServer();

            SubscribeLocalEvent<TechnologyDatabaseComponent, ResearchRegistrationChangedEvent>(OnDatabaseRegistrationChanged);
        }

        /* Removed for Persistence14
        /// <summary>
        /// Gets the names of all the servers.
        /// </summary>
        /// <returns></returns>
        public string[] GetServerNames(EntityUid client)
        {
            return GetServers(client).Select(x =>
            {
                if (!TryComp<ResearchServerComponent>(x, out var serverComp))
                    return "N/A";
                return serverComp.ServerName;
            }).ToArray();
        }

        /// <summary>
        /// Gets the ids of all the servers
        /// </summary>
        /// <returns></returns>
        public int[] GetServerIds(EntityUid client)
        {
            return GetServers(client).Select(x =>
            {
                if (!TryComp<ResearchServerComponent>(x, out var serverComp))
                    return -1;
                return serverComp.Id;
            }).ToArray();
        }

        public HashSet<EntityUid> GetServers(EntityUid client)
        {
            var clientXform = Transform(client);
            if (clientXform.GridUid is not { } grid)
                return [];

            var set = new HashSet<Entity<ResearchServerComponent>>();
            _lookup.GetGridEntities(grid, set);
            var final = new HashSet<EntityUid>();
            var clientStation = _station.GetOwningStation(client);
            foreach (var thing in set)
            {
                if (_station.GetOwningStation(thing.Owner) == clientStation)
                {
                    final.Add(thing);
                }
            }
            return final;
        }
        */

        public override void Update(float frameTime)
        {
            var query = EntityQueryEnumerator<ResearchServerComponent>();
            while (query.MoveNext(out var uid, out var server))
            {
                if (server.NextUpdateTime > _timing.CurTime)
                    continue;
                server.NextUpdateTime = _timing.CurTime + server.ResearchConsoleUpdateTime;

                UpdateServer(uid, (int)server.ResearchConsoleUpdateTime.TotalSeconds, server);
            }
        }
    }
}
