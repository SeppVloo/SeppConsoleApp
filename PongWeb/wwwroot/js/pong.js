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

function setTarget(y) {
    if (mode === "client") link.sendInput(y);
    else dotnet.invokeMethod("SetTarget", localSide, y);
}

// mode: "practice" | "host" | "client"
export function start(el, dotnetRef, gameMode, side, powerUpInfo) {
    stop();
    canvas = el; ctx = canvas.getContext("2d", { alpha: false });
    resize();
    resizeObs = new ResizeObserver(resize); resizeObs.observe(canvas);
    buffer.length = 0;
    trail.length = 0;
    dotnet = dotnetRef; mode = gameMode; localSide = side; powerUps = powerUpInfo;
    latest = null; frame = 0; lastTime = performance.now();
    last = { hit: -1, score: -1, power: -1, wall: -1 };

    canvas.onpointerdown = e => { canvas.setPointerCapture(e.pointerId); setTarget(toGameY(e.clientY)); e.preventDefault(); };
    canvas.onpointermove = e => { if (e.buttons || e.pointerType === "touch") setTarget(toGameY(e.clientY)); e.preventDefault(); };
    canvas.onpointerup = canvas.onpointercancel = () => setTarget(null);

    if (mode === "host") {
        link.onInput = y => dotnet.invokeMethod("SetTarget", 1, y);
        link.onCmd = m => { if (m.type === "restart") dotnet.invokeMethod("Restart"); };
    } else if (mode === "client") {
        link.onState = s => { buffer.push({ t: performance.now(), s }); if (buffer.length > 8) buffer.shift(); latest = s; };
    }

    raf = requestAnimationFrame(loop);
}

export function stop() {
    cancelAnimationFrame(raf);
    raf = 0;
    resizeObs?.disconnect(); resizeObs = null;
    link.onState = link.onInput = link.onCmd = null;
}

export function restart() {
    if (mode === "client") link.sendCmd({ type: "restart" });
    else dotnet.invokeMethod("Restart");
}

function loop(now) {
    raf = requestAnimationFrame(loop);
    const dt = Math.min((now - lastTime) / 1000, 0.1);
    lastTime = now;

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
        leftY: lerp(A.leftY, B.leftY, t), rightY: lerp(A.rightY, B.rightY, t),
        leftH: lerp(A.leftH, B.leftH, t), rightH: lerp(A.rightH, B.rightH, t),
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
        if (last.hit >= 0) { play("hit"); burst(bx, by, bx < W / 2 ? colorOf(0, s) : colorOf(1, s), 14, 220); }
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
        ctx.globalAlpha = 0.2; ctx.fillStyle = "#38bdf8";
        roundRect(w.x - 6, w.y - 6, 24, w.h + 12, 10); ctx.fill();
        ctx.globalAlpha = 0.35 + hp * 0.65;
        ctx.fillStyle = "#38bdf8";
        roundRect(w.x, w.y, 12, w.h, 5); ctx.fill();
        ctx.globalAlpha = 1;
        ctx.fillStyle = "rgba(11,16,38,0.8)";
        for (let k = 1; k < w.maxHp; k++) ctx.fillRect(w.x, w.y + w.h * k / w.maxHp - 1, 12, 2);
    }
    if (s.wallSeq !== last.wall) {
        if (last.wall >= 0) burst(bx, by, "#38bdf8", 12, 200);
        last.wall = s.wallSeq;
    }

    // Paddles
    paddle(20, s.leftY, s.leftH, 0, s);
    paddle(W - 32, s.rightY, s.rightH, 1, s);

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

    // Winner overlay
    if (s.winner >= 0) {
        ctx.fillStyle = "rgba(5,8,20,0.7)";
        ctx.fillRect(0, 0, W, H);
        const won = s.winner === localSide;
        glow(won ? C.me : C.them, 30);
        ctx.fillStyle = "#fff";
        ctx.font = "800 52px system-ui, sans-serif";
        ctx.fillText(won ? "Jij wint! 🎉" : "Verloren", W / 2, H / 2 - 12);
        noGlow();
        ctx.font = "500 18px system-ui, sans-serif";
        ctx.fillStyle = "rgba(255,255,255,0.7)";
        ctx.fillText(`${s.leftScore} – ${s.rightScore}  ·  tik op ↻ voor revanche`, W / 2, H / 2 + 34);
        if (won && Math.random() < 0.3) burst(Math.random() * W, -5, ["#22d3ee", "#f472b6", "#fde047"][Math.floor(Math.random() * 3)], 1, 120);
    }

    ctx.restore();
}

function colorOf(i, s) {
    if (i === 0 ? s.leftInverted : s.rightInverted) return C.inverted;
    return i === localSide ? C.me : C.them;
}

function paddle(x, cy, h, i, s) {
    const color = colorOf(i, s);
    ctx.globalAlpha = 0.18; ctx.fillStyle = color;
    roundRect(x - 6, cy - h / 2 - 6, 24, h + 12, 12); ctx.fill();
    ctx.globalAlpha = 1;
    const grad = ctx.createLinearGradient(x, 0, x + 12, 0);
    grad.addColorStop
    ctx.fillStyle = grad;
    roundRect(x, cy - h / 2, 12, h, 6);
    ctx.fill();
}
