// Serverless matchmaking + peer-to-peer transport.
// Players on the same Wi-Fi share one public IP, so they join the same Trystero room automatically.
// Trystero only uses free public signalling relays to set up a direct WebRTC connection;
// game data then flows directly between the devices.
// MQTT strategy: signalling via big public MQTT brokers (EMQX, HiveMQ, Mosquitto). These are far more
// reliable than Nostr relays, which often rate-limit or require a whitelist.
import { joinRoom, selfId, getRelaySockets } from "https://esm.sh/trystero@0.21.8/mqtt";

const APP_ID = "sepp-pong-v1";

let room, dotnet, me = { name: "", hosting: false, busy: false };
const peers = new Map(); // peerId -> { name, hosting, busy }
// Host: remote players by peer id. Client: the host's peer id.
const players = new Map(); // peerId -> { slot, name }
let host = null, needed = 0, freeSlots = [], fixedNames = [];
let sendHello, sendJoin, sendReply, sendState, sendInput, sendCmd;
let onHello, onJoin, onReply, onStateMsg, onInputMsg, onCmdMsg;
let onState = null, onInput = null, onCmd = null;
const diag = { room: "", ipOk: false, relays: 0, relaysTotal: 0, peers: 0, hellos: 0 };

function pushDiag() {
    try {
        const sockets = Object.values(getRelaySockets?.() ?? {});
        diag.relaysTotal = sockets.length;
        diag.relays = sockets.filter(s => s.readyState === 1 || s.connected === true).length;
    } catch { }
    diag.peers = room ? Object.keys(room.getPeers()).length : 0;
    dotnet?.invokeMethodAsync("OnDiag", { ...diag });
}

// Several IP lookup services: Safari/iOS content blockers or "Limit IP tracking" often block one of them.
const IP_SERVICES = [
    async () => (await (await fetch("https://api.ipify.org?format=json", { cache: "no-store" })).json()).ip,
    async () => (await (await fetch("https://api64.ipify.org?format=json", { cache: "no-store" })).json()).ip,
    async () => (await (await fetch("https://icanhazip.com", { cache: "no-store" })).text()).trim(),
    async () => /ip=([^\n]+)/.exec(await (await fetch("https://www.cloudflare.com/cdn-cgi/trace", { cache: "no-store" })).text())[1].trim(),
    async () => (await (await fetch("https://ifconfig.co/json", { cache: "no-store" })).json()).ip,
];

// Public IPv4 via a STUN server (WebRTC). No website involved, so Safari's tracker/IP protection doesn't block it.
function stunIp() {
    return new Promise(resolve => {
        let pc;
        const done = ip => { try { pc?.close(); } catch { } resolve(ip); };
        try {
            pc = new RTCPeerConnection({ iceServers: [{ urls: ["stun:stun.l.google.com:19302", "stun:stun.cloudflare.com:3478"] }] });
            pc.createDataChannel("x");
            pc.onicecandidate = e => {
                if (!e.candidate) return done(null);
                const m = / (\d{1,3}(?:\.\d{1,3}){3}) \d+ typ srflx/.exec(e.candidate.candidate);
                if (m) done(m[1]);
            };
            pc.createOffer().then(o => pc.setLocalDescription(o)).catch(() => done(null));
            setTimeout(() => done(null), 4000);
        } catch { done(null); }
    });
}

async function publicIp() {
    const s = await stunIp();
    if (s) return s;
    for (const svc of IP_SERVICES) {
        try {
            const ip = await Promise.race([svc(), new Promise((_, rej) => setTimeout(() => rej(), 4000))]);
            if (ip && /^[0-9a-f.:]+$/i.test(ip)) return ip;
        } catch { }
    }
    return null;
}

async function roomName() {
    const ip = await publicIp();
    if (!ip) return "lan-fallback";
    diag.ipOk = true;
    // IPv6 differs per device; use the /64 network prefix so devices on the same Wi-Fi still match.
    const key = ip.includes(":") ? ip.split(":").slice(0, 4).join(":") : ip;
    const hash = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(key));
    return "lan-" + [...new Uint8Array(hash)].slice(0, 8).map(b => b.toString(16).padStart(2, "0")).join("");
}

function pushPeers() {
    const list = [...peers.entries()].map(([id, p]) => ({ id, name: p.name, hosting: p.hosting && !p.busy }));
    dotnet?.invokeMethodAsync("OnPeers", list);
}

function announce(to) { sendHello?.({ name: me.name, hosting: me.hosting, busy: me.busy }, to); }

export async function start(dotnetRef, name) {
    dotnet = dotnetRef;
    me.name = name;
    lanRoom = await roomName();
    await connect(lanRoom);
    setInterval(pushDiag, 2000);
}

let lanRoom = "";

// Internet play: everyone who enters the same code ends up in the same room, wherever they are.
// Empty code = back to the automatic Wi-Fi room.
export async function setRoomCode(code) {
    code = (code || "").trim().toLowerCase();
    await connect(code ? "net-" + code : lanRoom);
}

async function connect(roomId) {
    if (room) { try { await room.leave(); } catch { } }
    peers.clear(); players.clear(); host = null; me.busy = false; me.hosting = false;
    pushPeers();
    room = joinRoom({ appId: APP_ID }, diag.room = roomId);
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
        dropPeer(id);
    });

    onHello((data, id) => { diag.hellos++; peers.set(id, data); pushPeers(); });

    onJoin((data, id) => {
        if (me.hosting && !me.busy && players.size < needed && !players.has(id)) {
            const slot = freeSlots.shift();
            players.set(id, { slot, name: data.name });
            sendReply({ ok: true, slot }, id);
            dotnet.invokeMethodAsync("OnLobbyPlayers", [...players.values()].map(p => p.name));
            if (players.size === needed) {
                me.busy = true;
                announce();
                const names = [...fixedNames, ...[...players.values()].map(p => ({ slot: p.slot, name: p.name }))];
                for (const pid of players.keys()) sendCmd({ type: "start", names }, pid);
                dotnet.invokeMethodAsync("OnGameStart", 0, names);
            }
        } else {
            sendReply({ ok: false }, id);
        }
    });

    onReply((data, id) => {
        if (data.ok) {
            host = id;
            me.busy = true;
            announce();
            mySlot = data.slot;
            dotnet.invokeMethodAsync("OnStatus", "Aangemeld! Wachten tot alle spelers er zijn...");
        } else {
            dotnet.invokeMethodAsync("OnStatus", "Dat spel is al vol.");
        }
    });

    onStateMsg((s, id) => { if (id === host) onState?.(s); });
    onInputMsg((m, id) => { const p = players.get(id); if (p) onInput?.(p.slot, m.y); });
    onCmdMsg((m, id) => {
        if (id === host) {
            if (m.type === "leave") endGame("De host is gestopt.");
            else if (m.type === "start") dotnet.invokeMethodAsync("OnGameStart", mySlot, m.names);
            else onCmd?.(m);
        } else if (players.has(id)) {
            if (m.type === "leave") dropPeer(id);
            else onCmd?.(m);
        }
    });

    announce();
}

let mySlot = 0;

function dropPeer(id) {
    if (id === host) { endGame("Verbinding met de host verbroken."); return; }
    const p = players.get(id);
    if (!p) return;
    players.delete(id);
    if (me.busy) {
        // Game running: the computer takes over this player's paddle.
        dotnet?.invokeMethodAsync("OnPlayerLeft", p.slot, p.name);
        if (players.size === 0) endGame("Alle andere spelers zijn gestopt.");
    } else {
        freeSlots.unshift(p.slot);
        freeSlots.sort((a, b) => a - b);
        dotnet?.invokeMethodAsync("OnLobbyPlayers", [...players.values()].map(p => p.name));
    }
}

function endGame(reason) {
    host = null;
    players.clear();
    me.busy = false;
    me.hosting = false;
    announce();
    dotnet?.invokeMethodAsync("OnGameEnd", reason);
}

export function setName(name) { me.name = name; announce(); }
// slots: the free slots remote players will get, in join order.
export function setHosting(on, slots, known) {
    me.hosting = on; me.busy = false;
    players.clear();
    freeSlots = [...(slots || [])];
    fixedNames = known || [];
    needed = freeSlots.length;
    announce();
}
export function join(peerId) { sendJoin({ name: me.name }, peerId); }

function others() { return host ? [host] : [...players.keys()]; }

export function leave() {
    for (const id of others()) sendCmd({ type: "leave" }, id);
    host = null;
    players.clear();
    me.busy = false;
    me.hosting = false;
    announce();
}

// Used by pong.js during a match
export const link = {
    sendState: s => { const ids = [...players.keys()]; if (ids.length) sendState(s, ids); },
    sendInput: y => host && sendInput({ y }, host),
    sendCmd: m => { const ids = others(); if (ids.length) sendCmd(m, ids); },
    set onState(f) { onState = f; },
    set onInput(f) { onInput = f; },
    set onCmd(f) { onCmd = f; },
};

export { selfId };
