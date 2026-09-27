// Serverless matchmaking + peer-to-peer transport.
// Everyone joins one shared lobby; players with the same public IP are marked as "on your Wi-Fi".
// (iOS often hides the real IP, so the IP is only a hint, not a separate room.)
// Trystero only uses free public signalling relays to set up a direct WebRTC connection;
// game data then flows directly between the devices.
import { joinRoom, selfId } from "https://esm.sh/trystero@0.21.8";

const APP_ID = "sepp-pong-v1";
const LOBBY = "lobby";

let room, dotnet, me = { name: "", hosting: false, busy: false, net: "" };
const peers = new Map(); // peerId -> { name, hosting, busy }
let opponent = null;
let sendHello, sendJoin, sendReply, sendState, sendInput, sendCmd;
let onHello, onJoin, onReply, onStateMsg, onInputMsg, onCmdMsg;
let onState = null, onInput = null, onCmd = null;

async function networkHash() {
    try {
        const r = await fetch("https://api.ipify.org?format=json", { cache: "no-store" });
        const { ip } = await r.json();
        const hash = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(ip));
        return [...new Uint8Array(hash)].slice(0, 8).map(b => b.toString(16).padStart(2, "0")).join("");
    } catch {
        return "";
    }
}

function pushPeers() {
    const list = [...peers.entries()].map(([id, p]) => ({
        id, name: p.name, hosting: p.hosting && !p.busy,
        sameNetwork: !!me.net && p.net === me.net,
    }));
    dotnet?.invokeMethodAsync("OnPeers", list);
}

function announce(to) { sendHello?.({ name: me.name, hosting: me.hosting, busy: me.busy, net: me.net }, to); }

export async function start(dotnetRef, name) {
    dotnet = dotnetRef;
    me.name = name;
    me.net = await networkHash();
    room = joinRoom({ appId: APP_ID }, LOBBY);
    setInterval(() => announce(), 3000);

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

    onHello((data, id) => { peers.set(id, data); pushPeers(); });

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
