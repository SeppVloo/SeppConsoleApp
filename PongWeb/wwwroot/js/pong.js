// Game loop, rendering and input. The C# PongEngine runs on the host (or in practice mode);
// the client only draws the states it receives and sends its finger position.
import { link } from "./net.js";

const W = 800, H = 450;
let canvas, ctx, dotnet, mode = null, localSide = 0, raf = 0;
let latest = null, frame = 0, lastTime = 0;
let last = { hit: -1, score: -1, power: -1 };
let powerFlash = { text: "", until: 0 };
let powerUps = [];
const sounds = {};

["hit", "score", "power"].forEach(n => { const a = new Audio(`sounds/${n}.wav`); a.preload = "auto"; sounds[n] = a; });
function play(n) { try { const c = sounds[n].cloneNode(); c.volume = 0.6; c.play().catch(() => { }); } catch { } }

function toGameY(clientY) {
    const r = canvas.getBoundingClientRect();
    return (clientY - r.top) / r.height * H;
}

function setTarget(y, slot = localSide) {
    if (mode === "client") link.sendInput(y, slot);
    else dotnet.invokeMethod("SetTarget", slot, y);
}

// ---------- Several players on one device ----------
let localSlots = [0], shared = false;
const pointerSlot = new Map();
const KEYS = [["KeyW", "KeyS"], ["ArrowUp", "ArrowDown"], ["KeyT", "KeyG"], ["KeyI", "KeyK"]];
const keysDown = new Set(), keyActive = new Set();
const keyY = [H / 2, H / 2, H / 2, H / 2];
const KEY_SPEED = 650;

// Touch: left half = left team, right half = right team; in 2v2 top/bottom picks the lane.
function slotAt(clientX, clientY) {
    if (localSlots.length === 1) return localSlots[0];
    const r = canvas.getBoundingClientRect();
    const team = clientX - r.left < r.width / 2 ? 0 : 1;
    const y = toGameY(clientY);
    const mine = localSlots.filter(s => s % 2 === team);
    if (mine.length === 0) return localSide;
    const lane = y < H / 2 ? 0 : 1;
    return mine.find(s => (s >> 1) === lane) ?? mine[0];
}

// One player on this device: both W/S and the arrow keys work. Otherwise every slot has its own pair.
function keysFor(slot) {
    if (localSlots.length === 1) return { up: ["KeyW", "ArrowUp"], down: ["KeyS", "ArrowDown"] };
    const [u, d] = KEYS[Math.max(0, localSlots.indexOf(slot))];
    return { up: [u], down: [d] };
}

function slotForKey(code) {
    return localSlots.find(s => { const k = keysFor(s); return k.up.includes(code) || k.down.includes(code); });
}

function onKey(e) {
    if (e.target instanceof HTMLInputElement) return;
    const slot = slotForKey(e.code);
    if (slot === undefined) return;
    if (e.type === "keydown") keysDown.add(e.code);
    else {
        keysDown.delete(e.code);
        const k = keysFor(slot);
        if (![...k.up, ...k.down].some(c => keysDown.has(c))) { keyActive.delete(slot); setTarget(null, slot); }
    }
    e.preventDefault();
}

function keyInput(dt) {
    for (const slot of localSlots) {
        const k = keysFor(slot);
        const dir = (k.down.some(c => keysDown.has(c)) ? 1 : 0) - (k.up.some(c => keysDown.has(c)) ? 1 : 0);
        if (!dir) continue;
        if (!keyActive.has(slot)) { keyActive.add(slot); keyY[slot] = latest?.paddles?.[slot]?.y ?? H / 2; }
        keyY[slot] = Math.max(0, Math.min(H, keyY[slot] + dir * KEY_SPEED * dt));
        setTarget(keyY[slot], slot);
    }
}

// mode: "practice" | "host" | "client"
export function start(el, dotnetRef, gameMode, side, powerUpInfo, slots) {
    stop();
    canvas = el; ctx = canvas.getContext("2d", { alpha: false });
    resize();
    resizeObs = new ResizeObserver(resize); resizeObs.observe(canvas);
    buffer.length = 0;
    trail.length = 0;
    dotnet = dotnetRef; mode = gameMode; localSide = side; powerUps = powerUpInfo;
    localSlots = slots && slots.length ? slots : [side];
    shared = localSlots.some(s => s % 2 === 0) && localSlots.some(s => s % 2 === 1);
    pointerSlot.clear(); keysDown.clear();
    latest = null; frame = 0; lastTime = performance.now();
    last = { hit: -1, score: -1, power: -1, wall: -1 };

    canvas.onpointerdown = e => {
        if (e.pointerType === "mouse" && localSlots.length > 1) return;
        canvas.setPointerCapture(e.pointerId);
        const slot = e.pointerType === "mouse" ? localSide : slotAt(e.clientX, e.clientY);
        pointerSlot.set(e.pointerId, slot);
        setTarget(toGameY(e.clientY), slot);
        e.preventDefault();
    };
    canvas.onpointermove = e => {
        if (!(e.buttons || e.pointerType !== "pen")) return;
        if (e.pointerType === "mouse" && localSlots.length > 1) return;
        const slot = pointerSlot.get(e.pointerId) ?? (e.pointerType === "mouse" ? localSide : slotAt(e.clientX, e.clientY));
        setTarget(toGameY(e.clientY), slot);
        e.preventDefault();
    };
    canvas.onpointerup = canvas.onpointercancel = e => {
        const slot = pointerSlot.get(e.pointerId) ?? localSide;
        pointerSlot.delete(e.pointerId);
        if (e.pointerType !== "mouse") setTarget(null, slot);
    };
    canvas.onpointerleave = e => { if (e.pointerType === "mouse" && localSlots.length === 1) setTarget(null, localSide); };
    window.addEventListener("keydown", onKey);
    window.addEventListener("keyup", onKey);

    if (mode === "host") {
        link.onInput = (slot, y) => dotnet.invokeMethod("SetTarget", slot, y);
        link.onCmd = m => { if (m.type === "restart") dotnet.invokeMethod("Restart"); else if (m.type === "pause") dotnet.invokeMethod("TogglePause"); };
    } else if (mode === "client") {
        link.onState = s => { buffer.push({ t: performance.now(), s }); if (buffer.length > 8) buffer.shift(); latest = s; };
    }

    raf = requestAnimationFrame(loop);
}

export function stop() {
    cancelAnimationFrame(raf);
    raf = 0;
    resizeObs?.disconnect(); resizeObs = null;
    window.removeEventListener("keydown", onKey);
    window.removeEventListener("keyup", onKey);
    keysDown.clear(); keyActive.clear();
    link.onState = link.onInput = link.onCmd = null;
}

export function togglePause() {
    if (mode === "client") link.sendCmd({ type: "pause" });
    else dotnet.invokeMethod("TogglePause");
}

export function restart() {
    if (mode === "client") link.sendCmd({ type: "restart" });
    else dotnet.invokeMethod("Restart");
}

function loop(now) {
    raf = requestAnimationFrame(loop);
    const dt = Math.min((now - lastTime) / 1000, 0.1);
    lastTime = now;

    keyInput(dt);
    frame++;
    if (mode !== "client") {
        latest = dotnet.invokeMethod("Tick", dt);
        if (mode === "host") link.sendState(latest);
    }
    if (latest) draw(latest, dt);
}

// ---------- Visuals ----------
const C = {
    bg1: "#0b1026", bg2: "#1a0b2e",
    me: "#22d3ee", them: "#f472b6", inverted: "#a78bfa",
    ball: "#ffffff", fast: "#fb923c", line: "rgba(255,255,255,0.12)",
};
const trail = [];
const particles = [];
let shake = 0, view = null, scorePulse = [0, 0], lastScores = [0, 0];

let resizeObs = null, scale = 1, bgCache = null;
const buffer = [];
const sprites = {};

function lerp(a, b, t) { return a + (b - a) * t; }

// Match the backing store to the real on-screen pixel size so nothing is upscaled (= blurry).
function resize() {
    const r = canvas.getBoundingClientRect();
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const pw = Math.max(1, Math.round(r.width * dpr)), ph = Math.max(1, Math.round(r.height * dpr));
    if (canvas.width === pw && canvas.height === ph && bgCache) return;
    canvas.width = pw; canvas.height = ph;
    scale = pw / W;
    ctx.setTransform(scale, 0, 0, ph / H, 0, 0);
    ctx.imageSmoothingEnabled = true;
    buildBackground(pw, ph);
    for (const k in sprites) delete sprites[k];
}

function buildBackground(pw, ph) {
    bgCache = document.createElement("canvas");
    bgCache.width = pw; bgCache.height = ph;
    const b = bgCache.getContext("2d");
    b.setTransform(pw / W, 0, 0, ph / H, 0, 0);
    const g = b.createLinearGradient(0, 0, W, H);
    g.addColorStop(0, C.bg1); g.addColorStop(1, C.bg2);
    b.fillStyle = g; b.fillRect(0, 0, W, H);
    b.strokeStyle = "rgba(255,255,255,0.035)"; b.lineWidth = 1;
    b.beginPath();
    for (let x = 0; x <= W; x += 40) { b.moveTo(x, 0); b.lineTo(x, H); }
    for (let y = 0; y <= H; y += 40) { b.moveTo(0, y); b.lineTo(W, y); }
    b.stroke();
    b.strokeStyle = C.line; b.lineWidth = 2;
    b.setLineDash([6, 10]);
    b.beginPath(); b.moveTo(W / 2, 0); b.lineTo(W / 2, H); b.stroke();
    b.setLineDash([]);
    b.beginPath(); b.arc(W / 2, H / 2, 50, 0, Math.PI * 2); b.stroke();
}

// Pre-rendered glowing circle: shadowBlur per frame is very slow on phones.
function glowSprite(color, r, blur) {
    const key = color + r + "_" + blur;
    if (sprites[key]) return sprites[key];
    const size = Math.ceil((r + blur) * 2 * scale);
    const c = document.createElement("canvas");
    c.width = c.height = size;
    const g = c.getContext("2d");
    g.scale(scale, scale);
    const m = r + blur;
    const rg = g.createRadialGradient(m, m, r * 0.6, m, m, m);
    rg.addColorStop(0, color); rg.addColorStop(1, "rgba(0,0,0,0)");
    g.globalAlpha = 0.55; g.fillStyle = rg;
    g.beginPath(); g.arc(m, m, m, 0, Math.PI * 2); g.fill();
    g.globalAlpha = 1; g.fillStyle = color;
    g.beginPath(); g.arc(m, m, r, 0, Math.PI * 2); g.fill();
    return sprites[key] = { c, m };
}

function drawSprite(sp, x, y) { ctx.drawImage(sp.c, x - sp.m, y - sp.m, sp.m * 2, sp.m * 2); }

// Client: render slightly in the past and interpolate between two received states.
const DELAY = 50;
function smooth(s) {
    if (mode !== "client" || buffer.length < 2) return s;
    const rt = performance.now() - DELAY;
    let i = buffer.length - 1;
    while (i > 0 && buffer[i - 1].t > rt) i--;
    if (i === 0) return buffer[0].s;
    const a = buffer[i - 1], b = buffer[i];
    if (rt >= b.t) return b.s;
    const t = (rt - a.t) / (b.t - a.t);
    const A = a.s, B = b.s;
    if (A.scoreSeq !== B.scoreSeq || Math.abs(A.ballX - B.ballX) > 120) return B;
    return {
        ...B,
        ballX: lerp(A.ballX, B.ballX, t), ballY: lerp(A.ballY, B.ballY, t),
        paddles: B.paddles.map((p, i) => A.paddles[i] ? { ...p, y: lerp(A.paddles[i].y, p.y, t), h: lerp(A.paddles[i].h, p.h, t) } : p),
    };
}

function burst(x, y, color, count, speed) {
    for (let i = 0; i < count; i++) {
        const a = Math.random() * Math.PI * 2, v = speed * (0.4 + Math.random() * 0.6);
        particles.push({ x, y, vx: Math.cos(a) * v, vy: Math.sin(a) * v, life: 1, color, r: 1.5 + Math.random() * 2.5 });
    }
}

function roundRect(x, y, w, h, r) {
    ctx.beginPath();
    ctx.roundRect ? ctx.roundRect(x, y, w, h, r) : ctx.rect(x, y, w, h);
}

function glow(color, blur) { ctx.shadowColor = color; ctx.shadowBlur = blur; }
function noGlow() { ctx.shadowBlur = 0; }

function draw(raw, dt) {
    const s = smooth(raw, dt);
    const now = performance.now();
    const bx = s.ballX + 7, by = s.ballY + 7;

    if (s.hitSeq !== last.hit) {
        burst(bx, by, teamColor(bx < W / 2 ? 0 : 1), 14, 220);
        last.hit = s.hitSeq;
    }
    if (s.scoreSeq !== last.score) {
        if (last.score >= 0) {
            play("score"); shake = 12; trail.length = 0;
            scorePulse[s.leftScore !== lastScores[0] ? 0 : 1] = 1;
            navigator.vibrate?.(60);
        }
        last.score = s.scoreSeq;
    }
    lastScores = [s.leftScore, s.rightScore];
    if (s.powerSeq !== last.power) {
        if (last.power >= 0) {
            play("power"); powerFlash = { text: s.powerText, until: now + 1800 };
            burst(bx, by, "#fde047", 26, 300);
        }
        last.power = s.powerSeq;
    }

    ctx.save();
    if (shake > 0.2) { ctx.translate((Math.random() - 0.5) * shake, (Math.random() - 0.5) * shake); shake *= Math.pow(0.001, dt); }

    // Background (cached)
    ctx.fillStyle = C.bg1; ctx.fillRect(-20, -20, W + 40, H + 40);
    ctx.drawImage(bgCache, 0, 0, W, H);

    // Scores
    ctx.textAlign = "center"; ctx.textBaseline = "middle";
    [[s.leftScore, W / 2 - 90, 0], [s.rightScore, W / 2 + 90, 1]].forEach(([v, x, i]) => {
        const p = scorePulse[i]; scorePulse[i] = Math.max(0, p - dt * 2);
        ctx.font = `800 ${56 + p * 24}px system-ui, -apple-system, sans-serif`;
        ctx.fillStyle = `rgba(255,255,255,${0.18 + p * 0.6})`;
        ctx.fillText(v, x, 60);
    });

    // Wall
    for (const w of (s.walls || [])) {
        const hp = w.hp / w.maxHp;
        const col = teamColor(w.owner);
        const ghost = w.side === w.owner; // own half: owner's ball passes through
        ctx.save();
        ctx.translate(w.x + 6, w.y + w.h / 2);
        ctx.rotate(w.angle || 0);
        ctx.globalAlpha = 0.2; ctx.fillStyle = col;
        roundRect(-12, -w.h / 2 - 6, 24, w.h + 12, 10); ctx.fill();
        ctx.globalAlpha = 0.35 + hp * 0.65;
        if (ghost) {
            ctx.strokeStyle = col; ctx.lineWidth = 2; ctx.setLineDash([6, 4]);
            roundRect(-5, -w.h / 2, 10, w.h, 5); ctx.stroke(); ctx.setLineDash([]);
            ctx.globalAlpha *= 0.35; roundRect(-6, -w.h / 2, 12, w.h, 5); ctx.fill();
        } else {
            roundRect(-6, -w.h / 2, 12, w.h, 5); ctx.fill();
        }
        ctx.globalAlpha = 1;
        ctx.fillStyle = "rgba(11,16,38,0.8)";
        for (let k = 1; k < w.maxHp; k++) ctx.fillRect(-6, -w.h / 2 + w.h * k / w.maxHp - 1, 12, 2);
        ctx.restore();
    }
    if (s.bumpSeq !== last.bump) {
        if (last.bump !== undefined) { play("hit"); shake = Math.max(shake, 6); navigator.vibrate?.(30); }
        last.bump = s.bumpSeq;
    }
    if (s.wallSeq !== last.wall) {
        if (last.wall >= 0) burst(bx, by, "#38bdf8", 12, 200);
        last.wall = s.wallSeq;
    }

    // Paddles
    s.paddles.forEach((p, i) => paddle(p.x, p.y, p.h, p, localSlots.includes(i)));
    if (localSlots.length > 1 && frame < 400 && !matchMedia("(pointer: coarse)").matches) {
        const labels = ["W/S", "↑/↓", "T/G", "I/K"];
        ctx.font = "700 14px system-ui, sans-serif";
        ctx.fillStyle = "rgba(255,255,255,0.8)";
        localSlots.forEach((slot, idx) => {
            const p = s.paddles[slot];
            if (p) ctx.fillText(labels[idx], p.x + (slot % 2 === 0 ? 40 : -28), p.y);
        });
    }

    // Power-up
    for (const pu of (s.powerUps || [])) {
        const info = powerUps[pu.type] || { color: "#fff", symbol: "?" };
        const cx = pu.x + 15, cy = pu.y + 15;
        const pulse = 1 + Math.sin(now / 180) * 0.1;
        ctx.save();
        ctx.translate(cx, cy);
        ctx.rotate(now / 1200);
        ctx.strokeStyle = info.color; ctx.lineWidth = 2.5;
        ctx.beginPath(); ctx.arc(0, 0, 19 * pulse, 0, Math.PI * 1.4); ctx.stroke();
        ctx.restore();
        const sp = glowSprite(info.color, 13, 12);
        ctx.drawImage(sp.c, cx - sp.m * pulse, cy - sp.m * pulse, sp.m * 2 * pulse, sp.m * 2 * pulse);
        ctx.fillStyle = "#0b1026";
        ctx.font = "700 16px system-ui, sans-serif";
        ctx.fillText(info.symbol, cx, cy + 1);
    }

    // Ball trail
    const ballColor = s.ballSpeedFactor > 1 ? C.fast : C.ball;
    const visible = !s.serving || Math.floor(now / 180) % 2 === 0;
    if (!s.serving) { trail.push({ x: bx, y: by }); if (trail.length > 14) trail.shift(); }
    else trail.length = 0;
    trail.forEach((p, i) => {
        const a = i / trail.length;
        ctx.fillStyle = s.ballSpeedFactor > 1 ? `rgba(251,146,60,${a * 0.35})` : `rgba(255,255,255,${a * 0.25})`;
        ctx.beginPath(); ctx.arc(p.x, p.y, 7 * a, 0, Math.PI * 2); ctx.fill();
    });

    // Ball
    if (visible) {
        drawSprite(glowSprite(ballColor, 7, 12), bx, by);
        ctx.fillStyle = "#fff";
        ctx.beginPath(); ctx.arc(bx, by, 4.5, 0, Math.PI * 2); ctx.fill();
    }

    // Particles
    for (let i = particles.length - 1; i >= 0; i--) {
        const p = particles[i];
        p.x += p.vx * dt; p.y += p.vy * dt; p.vx *= 0.96; p.vy *= 0.96; p.life -= dt * 1.8;
        if (p.life <= 0) { particles.splice(i, 1); continue; }
        ctx.globalAlpha = p.life;
        ctx.fillStyle = p.color;
        ctx.beginPath(); ctx.arc(p.x, p.y, p.r, 0, Math.PI * 2); ctx.fill();
    }
    ctx.globalAlpha = 1;

    // Power-up toast
    if (now < powerFlash.until) {
        const t = Math.min(1, (powerFlash.until - now) / 300);
        ctx.font = "700 18px system-ui, sans-serif";
        const w = ctx.measureText(powerFlash.text).width + 36;
        ctx.globalAlpha = t;
        ctx.fillStyle = "rgba(15,23,42,0.8)";
        roundRect(W / 2 - w / 2, H - 58, w, 36, 18); ctx.fill();
        ctx.strokeStyle = "rgba(253,224,71,0.6)"; ctx.lineWidth = 1.5; ctx.stroke();
        ctx.fillStyle = "#fde047";
        ctx.fillText("⚡ " + powerFlash.text, W / 2, H - 40);
        ctx.globalAlpha = 1;
    }

    // Pause overlay
    if (s.paused && s.winner < 0) {
        ctx.fillStyle = "rgba(5,8,20,0.6)";
        ctx.fillRect(0, 0, W, H);
        ctx.fillStyle = "#fff";
        ctx.font = "800 48px system-ui, sans-serif";
        ctx.fillText("⏸ Pauze", W / 2, H / 2 - 10);
        ctx.font = "500 18px system-ui, sans-serif";
        ctx.fillStyle = "rgba(255,255,255,0.7)";
        ctx.fillText("tik op ▶ om verder te spelen", W / 2, H / 2 + 30);
    }

    // Winner overlay + drone show
    if (s.winner >= 0) {
        ctx.fillStyle = "rgba(2,4,14,0.85)";
        ctx.fillRect(0, 0, W, H);
        const won = shared || s.winner === localSide % 2;
        droneShow(s.winnerName || (won ? "Jij" : "Winnaar"), won ? C.me : C.them, dt, now);
        ctx.fillStyle = "#fff";
        ctx.font = "800 30px system-ui, sans-serif";
        ctx.fillText(shared ? `${s.winnerName || "Winnaar"} wint! 🎉` : won ? "Jij wint! 🎉" : "Verloren", W / 2, H - 70);
        ctx.font = "500 16px system-ui, sans-serif";
        ctx.fillStyle = "rgba(255,255,255,0.7)";
        ctx.fillText(`${s.leftScore} – ${s.rightScore}  ·  tik op ↻ voor revanche`, W / 2, H - 38);
    } else drones = null;

    ctx.restore();
}

// ---------- Drone show ----------
let drones = null;

// Sample the name's pixels into target points for the drones.
function nameTargets(text) {
    const c = document.createElement("canvas");
    c.width = W; c.height = 260;
    const g = c.getContext("2d");
    let size = 150;
    g.font = `900 ${size}px system-ui, sans-serif`;
    while (g.measureText(text).width > W - 60 && size > 30) { size -= 6; g.font = `900 ${size}px system-ui, sans-serif`; }
    g.textAlign = "center"; g.textBaseline = "middle"; g.fillStyle = "#fff";
    g.fillText(text, W / 2, 130);
    const data = g.getImageData(0, 0, W, 260).data;
    const pts = [], step = size > 90 ? 7 : 5;
    for (let y = 0; y < 260; y += step)
        for (let x = 0; x < W; x += step)
            if (data[(y * W + x) * 4 + 3] > 128) pts.push({ x, y: y + 40 });
    for (let i = pts.length - 1; i > 0; i--) { const j = Math.floor(Math.random() * (i + 1)); [pts[i], pts[j]] = [pts[j], pts[i]]; }
    return pts.slice(0, 600);
}

function droneShow(name, color, dt, now) {
    if (!drones || drones.name !== name) drones = newDroneShow(name, color, now);
    const S = drones;
    const t = (now - S.start) / 1000;

    // Advance to the next scene; after the playlist ends, a fresh random one is drawn (name always in between).
    while (t - S.sceneStart >= S.scenes[S.idx].dur) {
        S.sceneStart += S.scenes[S.idx].dur;
        if (++S.idx >= S.scenes.length) { S.scenes = droneScenes(S, false); S.idx = 0; }
        prepareScene(S, S.scenes[S.idx]);
    }
    const sc = S.scenes[S.idx], st = t - S.sceneStart;
    const sp = {};
    const k = 1 - Math.pow(sc.ease, dt);
    const n = S.list.length;
    for (let i = 0; i < n; i++) {
        const d = S.list[i];
        const u = i / n;
        let gx, gy;
        if (st < sc.delay(u)) { gx = d.x; gy = d.y; }      // staggered arrival: hover in place until it's your turn
        else switch (sc.kind) {
            case "rise": gx = d.x; gy = H * 0.9 - (st / sc.dur) * H * 0.3 - d.lane * 20; break;
            case "rings": {
                const a = d.a + st * S.spinSpeed * (d.lane % 2 ? S.spinDir : -S.spinDir);
                const r = 60 + d.lane * 22;
                gx = W / 2 + Math.cos(a) * r * 1.6; gy = H * 0.4 + Math.sin(a) * r * 0.8; break;
            }
            case "spiral": {
                const a = d.a * 3 + st * S.spinSpeed * S.spinDir;
                const r = 20 + u * Math.min(W, H) * 0.42;
                gx = W / 2 + Math.cos(a) * r; gy = H * 0.42 + Math.sin(a) * r * 0.7; break;
            }
            case "wave":
                gx = W * (0.05 + 0.9 * ((u * 7) % 1));
                gy = H * (0.2 + 0.08 * Math.floor(u * 7)) + Math.sin(gx / 60 - st * 3 * S.spinDir) * 25; break;
            case "sphere": {                               // rotating 3D globe
                const lat = Math.acos(1 - 2 * u), lon = d.a * 13 + st * S.spinSpeed * S.spinDir;
                const R = Math.min(W, H) * 0.3;
                gx = W / 2 + Math.sin(lat) * Math.cos(lon) * R; gy = H * 0.42 + Math.cos(lat) * R; break;
            }
            default:                                       // fixed shapes: name, trophy, star, heart
                gx = d.tx; gy = d.ty + Math.sin(now / 500 + d.tx / 50) * 3;
        }
        d.x += (gx - d.x) * k;
        d.y += (gy - d.y) * k;

        let hue = S.palette[d.lane % S.palette.length];
        if (sc.kind === "name" && st > 3) {
            hue = droneColorFx(S, d, u, now);
        } else if (sc.kind === "trophy") hue = "#fde047";
        else if (sc.kind === "heart") hue = "#f43f5e";
        ctx.globalAlpha = 0.65 + 0.35 * Math.sin(now / 220 + d.a * 7);
        drawSprite(sp[hue] ??= glowSprite(hue, 1.8, 4), d.x, d.y);
    }
    ctx.globalAlpha = 1;
    if (sc.kind !== "rise" && Math.random() < S.fireworks)
        burst(W * (0.1 + 0.8 * Math.random()), 30 + Math.random() * 60, S.palette[Math.floor(Math.random() * S.palette.length)], 24, 140);
}

const pick = a => a[Math.floor(Math.random() * a.length)];
const shuffle = a => { for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(Math.random() * (i + 1)); [a[i], a[j]] = [a[j], a[i]]; } return a; };

// Every show gets its own palette, spin direction/speed, colour effect, fireworks rate and scene order.
function newDroneShow(name, color, now) {
    const text = nameTargets(name);
    const n = text.length;
    const S = {
        name, color, start: now, sceneStart: 0, idx: 0, nameTargets: text,
        palette: [color, ...pick([["#fde047", "#ffffff"], ["#22d3ee", "#f472b6"], ["#a3e635", "#fde047"], ["#c084fc", "#ffffff"], ["#fb923c", "#facc15"]])],
        spinDir: Math.random() < 0.5 ? 1 : -1,
        spinSpeed: 0.6 + Math.random() * 0.8,
        colorFx: pick(["wave", "sparkle", "rainbow", "pulse"]),
        fireworks: 0.015 + Math.random() * 0.035,
        list: [],
    };
    const start = pick(["bottom", "sides", "center"]);
    for (let i = 0; i < n; i++) {
        let x, y;
        if (start === "bottom") { x = W * (0.1 + 0.8 * Math.random()); y = H + 20 + Math.random() * 60; }
        else if (start === "sides") { x = i % 2 ? -20 - Math.random() * 60 : W + 20 + Math.random() * 60; y = H * (0.3 + 0.6 * Math.random()); }
        else { x = W / 2 + (Math.random() - 0.5) * 20; y = H + 20; }
        S.list.push({ x, y, tx: 0, ty: 0, a: (i / n) * Math.PI * 2, lane: i % 5 });
    }
    S.scenes = droneScenes(S, true);
    prepareScene(S, S.scenes[0]);
    return S;
}

// A random playlist: (rise) → effect → name → effect/shape → name → ...
function droneScenes(S, intro) {
    const dyn = shuffle(["rings", "spiral", "wave", "sphere"]);
    const shapes = shuffle(["trophy", "star", "heart"]);
    const stagger = () => pick([u => 0, u => u * 3, u => (1 - u) * 3, u => Math.abs(u - 0.5) * 4]);
    const scene = kind => ({ kind, dur: kind === "name" ? 8 + Math.random() * 4 : 4 + Math.random() * 3, ease: 0.08 + Math.random() * 0.08, delay: kind === "name" ? stagger() : () => 0 });
    const list = intro ? [scene("rise")] : [];
    list.push(scene(dyn[0]), scene("name"), scene(Math.random() < 0.6 ? shapes[0] : dyn[1]), scene("name"), scene(shapes[1]));
    if (intro) list[0].dur = 3;
    return list;
}

function prepareScene(S, sc) {
    let pts = null;
    if (sc.kind === "name") pts = S.nameTargets;
    else if (sc.kind === "trophy") pts = trophyTargets(S.list.length);
    else if (sc.kind === "star") pts = starTargets(S.list.length);
    else if (sc.kind === "heart") pts = heartTargets(S.list.length);
    if (pts) S.list.forEach((d, i) => { d.tx = pts[i].x; d.ty = pts[i].y; });
}

function droneColorFx(S, d, u, now) {
    switch (S.colorFx) {
        case "sparkle": return Math.random() < 0.04 ? "#ffffff" : S.color;
        case "rainbow": return `hsl(${Math.floor((d.tx / W * 360 + now / 10) % 360 / 30) * 30}, 90%, 65%)`;
        case "pulse": return Math.sin(now / 300) > 0.3 ? S.palette[1] : S.color;
        default: { const w = Math.sin(d.tx / 70 - now / 350 * S.spinDir); return w > 0.6 ? S.palette[2] : w < -0.6 ? S.palette[1] : S.color; }
    }
}

function starTargets(n) {
    const pts = [], cx = W / 2, cy = H * 0.4, R = Math.min(W, H) * 0.3;
    for (let i = 0; i < n; i++) {
        const u = (i / n) * 10, seg = Math.floor(u), f = u - seg;
        const a0 = -Math.PI / 2 + seg * Math.PI / 5, a1 = a0 + Math.PI / 5;
        const r0 = seg % 2 ? R * 0.42 : R, r1 = seg % 2 ? R : R * 0.42;
        pts.push({ x: cx + Math.cos(a0) * r0 * (1 - f) + Math.cos(a1) * r1 * f, y: cy + Math.sin(a0) * r0 * (1 - f) + Math.sin(a1) * r1 * f });
    }
    return pts;
}

function heartTargets(n) {
    const pts = [], cx = W / 2, cy = H * 0.38, s = Math.min(W, H) * 0.018;
    for (let i = 0; i < n; i++) {
        const a = (i / n) * Math.PI * 2, rr = i % 3 ? 1 : 0.6 + Math.random() * 0.4;
        pts.push({ x: cx + 16 * Math.pow(Math.sin(a), 3) * s * rr, y: cy - (13 * Math.cos(a) - 5 * Math.cos(2 * a) - 2 * Math.cos(3 * a) - Math.cos(4 * a)) * s * rr });
    }
    return pts;
}

// Points outlining a trophy in the sky.
function trophyTargets(n) {
    const pts = [], cx = W / 2, top = 70;
    const add = (x, y) => pts.push({ x, y });
    for (let i = 0; i < n; i++) {
        const u = i / n;
        if (u < 0.45) {                     // cup bowl (filled half-ellipse)
            const a = Math.PI * (u / 0.45), rr = Math.sqrt(Math.random());
            add(cx + Math.cos(a) * 90 * rr * (Math.random() < 0.5 ? 1 : -1), top + Math.sin(a) * 110 * rr);
        } else if (u < 0.6) {               // handles
            const a = ((u - 0.45) / 0.15) * Math.PI * 2;
            add(cx + (a < Math.PI ? -110 : 110) + Math.cos(a * 2) * 22, top + 40 + Math.sin(a * 2) * 30);
        } else if (u < 0.75) {              // stem
            add(cx + (Math.random() - 0.5) * 18, top + 110 + Math.random() * 50);
        } else {                            // base
            add(cx + (Math.random() - 0.5) * 140, top + 160 + Math.random() * 22);
        }
    }
    return pts;
}

function teamColor(team) { return team === localSide % 2 ? C.me : C.them; }

function paddle(x, cy, h, p, mine) {
    const color = p.inverted ? C.inverted : teamColor(p.team);
    ctx.globalAlpha = mine ? 0.3 : 0.18; ctx.fillStyle = color;
    roundRect(x - 6, cy - h / 2 - 6, 24, h + 12, 12); ctx.fill();
    ctx.globalAlpha = 1;
    const grad = ctx.createLinearGradient(x, 0, x + 12, 0);
    grad.addColorStop(0, color); grad.addColorStop(1, mine ? "#ffffff" : color);
    ctx.fillStyle = grad;
    roundRect(x, cy - h / 2, 12, h, 6);
    ctx.fill();
}
