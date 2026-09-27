// Serverless matchmaking + peer-to-peer transport.
// Players on the same Wi-Fi share one public IP, so they join the same Trystero room automatically.
// Trystero only uses free public signalling relays to set up a direct WebRTC connection;
// game data then flows directly between the devices.
import { joinRoom, selfId, getRelaySockets } from "https://esm.sh/trystero@0.21";

const APP_ID = "sepp-pong-v1";

let room, dotnet, me = { name: "", hosting: false, busy: false };
const peers = new Map(); // peerId -> { name, hosting, busy }
let opponent = null;
let sendHello, sendJoin, sendReply, sendState, sendInput, sendCmd;
let onHello, onJoin, onReply, onStateMsg, onInputMsg, onCmdMsg;
let onState = null, onInput = null, onCmd = null;
const diag = { room: "", ipOk: false, relays: 0, relaysTotal: 0, peers: 0, hellos: 0 };

function pushDiag() {
    try {
        const sockets = Object.values(getRelaySockets?.() ?? {});
        diag.relaysTotal = sockets.length;
        diag.relays = sockets.filter(s => s.readyState === 1).length;
    } catch { }
    diag.peers = room ? Object.keys(room.getPeers()).length : 0;
    dotnet?.invokeMethodAsync("OnDiag", { ...diag });
}

async function roomName() {
    try {
        const r = await fetch("https://api.ipify.org?format=json", { cache: "no-store" });
        const { ip } = await r.json();
        diag.ipOk = true;
        const hash = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(ip));
        return "lan-" + [...new Uint8Array(hash)].slice(0, 8).map(b => b.toString(16).padStart(2, "0")).join("");
    } catch {
        return "lan-fallback";
    }
}

function pushPeers() {
    const list = [...peers.entries()].map(([id, p]) => ({ id, name: p.name, hosting: p.hosting && !p.busy }));
    dotnet?.invokeMethodAsync("OnPeers", list);
}

function announce(to) { sendHello?.({ name: me.name, hosting: me.hosting, busy: me.busy }, to); }

export async function start(dotnetRef, name) {
    dotnet = dotnetRef;
    me.name = name;
    room = joinRoom({ appId: APP_ID }, diag.room = await roomName());
    setInterval(pushDiag, 2000);
    pushDiag();

    [sendHello, onHello] = room.makeAction("hello");
    [sendJoin, onJoin] = room.makeAction("join");
    [sendReply, onReply] = room.makeAction("reply");
    [sendState, onStateMsg] = room.makeAction("state");
    [sendInput, onInputMsg] = room.makeAction("input");
    [sendCmd, onCmdMsg] = room.makeAction("cmd");

    room.onPeerJoin(id => announce(id));
    room.onPeerLeave(id => {
        peers.delete(id);
        pushPeers();
        if (id === opponent) endGame("Verbinding met tegenstander verbroken.");
    });

    onHello((data, id) => { diag.hellos++; peers.set(id, data); pushPeers(); });

    onJoin((data, id) => {
        if (me.hosting && !me.busy) {
            me.busy = true;
            opponent = id;
            sendReply({ ok: true, name: me.name }, id);
            announce();
            dotnet.invokeMethodAsync("OnGameStart", 0, data.name);
        } else {
            sendReply({ ok: false }, id);
        }
    });

    onReply((data, id) => {
        if (data.ok) {
            opponent = id;
            me.busy = true;
            announce();
            dotnet.invokeMethodAsync("OnGameStart", 1, data.name);
        } else {
            dotnet.invokeMethodAsync("OnStatus", "Dat spel is al bezet.");
        }
    });

    onStateMsg((s, id) => { if (id === opponent) onState?.(s); });
    onInputMsg((m, id) => { if (id === opponent) onInput?.(m.y); });
    onCmdMsg((m, id) => {
        if (id !== opponent) return;
        if (m.type === "leave") endGame("Tegenstander is gestopt.");
        else onCmd?.(m);
    });

    announce();
}

function endGame(reason) {
    opponent = null;
    me.busy = false;
    me.hosting = false;
    announce();
    dotnet?.invokeMethodAsync("OnGameEnd", reason);
}

export function setName(name) { me.name = name; announce(); }
export function setHosting(on) { me.hosting = on; me.busy = false; announce(); }
export function join(peerId) { sendJoin({ name: me.name }, peerId); }

export function leave() {
    if (opponent) sendCmd({ type: "leave" }, opponent);
    opponent = null;
    me.busy = false;
    me.hosting = false;
    announce();
}

// Used by pong.js during a match
export const link = {
    sendState: s => opponent && sendState(s, opponent),
    sendInput: y => opponent && sendInput({ y }, opponent),
    sendCmd: m => opponent && sendCmd(m, opponent),
    set onState(f) { onState = f; },
    set onInput(f) { onInput = f; },
    set onCmd(f) { onCmd = f; },
};

export { selfId };
