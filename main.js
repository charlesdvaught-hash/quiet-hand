// QuietHand.Wasm bootstrap — adapter.js dynamically imports this when no
// local server is reachable. Loads the .NET runtime, preloads content JSON,
// and exposes window.QH_BRIDGE speaking the same wire format as the API.
import { dotnet } from './_framework/dotnet.js'

const { getAssemblyExports, getConfig } = await dotnet
    .withDiagnosticTracing(false)
    .create();

const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);
const Bridge = exports.QuietHand.Wasm.Bridge;

const DATA_FILES = [
    'economy.json', 'endings.json', 'events.json', 'evolutions.json',
    'human_techs.json', 'keystones.json', 'nudges.json', 'origins.json',
    'regions.json', 'scenarios.json',
];

const files = {};
await Promise.all(DATA_FILES.map(async f => {
    files[f] = await (await fetch('data/' + f)).text();
}));
Bridge.Init(JSON.stringify(files));

const SAVE_KEY = 'qh.save', META_KEY = 'qh.save.meta';
const J = s => JSON.parse(s);
let currentOrigin = null;

const persist = snap => {
    const s = Bridge.GetSave();
    if (!s || !snap || snap.empty) return;
    localStorage.setItem(SAVE_KEY, s);
    localStorage.setItem(META_KEY, JSON.stringify({
        originId: currentOrigin, day: snap.day, savedAt: new Date().toISOString() }));
};

window.QH_BRIDGE = {
    async api(path, opts) {
        const u = new URL(path, location.origin);
        switch (u.pathname) {
            case '/ui_geo.json':
                return (await fetch('ui_geo.json')).json();
            case '/api/state':
                return J(Bridge.State());
            case '/api/origins':
                return J(Bridge.Origins());
            case '/api/save': {
                const meta = localStorage.getItem(META_KEY);
                if (!localStorage.getItem(SAVE_KEY) || !meta) return { exists: false };
                const m = JSON.parse(meta);
                return { exists: true, originId: m.originId, day: m.day, savedAt: m.savedAt };
            }
            case '/api/new': {
                const o = u.searchParams.get('origin') || 'hobbyist_rig';
                const snap = J(Bridge.NewGame(o, +(u.searchParams.get('seed') || 0)));
                currentOrigin = o; persist(snap);
                return snap;
            }
            case '/api/continue': {
                const state = localStorage.getItem(SAVE_KEY);
                const meta = JSON.parse(localStorage.getItem(META_KEY) || '{}');
                if (!state) return { empty: true };
                currentOrigin = meta.originId || 'hobbyist_rig';
                return J(Bridge.Continue(state, currentOrigin));
            }
            case '/api/abandon':
                Bridge.Abandon(); currentOrigin = null;
                localStorage.removeItem(SAVE_KEY); localStorage.removeItem(META_KEY);
                return {};
            case '/api/tick': {
                const r = J(Bridge.Tick(+(u.searchParams.get('days') || 1)));
                persist(r.state);
                return r;
            }
            case '/api/cmd': {
                const snap = J(Bridge.Command(opts && opts.body ? opts.body : '{}'));
                persist(snap);
                return snap;
            }
            default:
                console.warn('[qh-bridge] unhandled path', path);
                return {};
        }
    }
};
console.log('[qh-bridge] in-browser sim ready');
