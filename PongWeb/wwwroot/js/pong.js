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
    canvas = el; ctx = canvas.getContext("2d");
    const dpr = Math.min(window.devicePixelRatio || 1, 3);
    canvas.width = W * dpr; canvas.height = H * dpr;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    trail.length = 0; particles.length = 0; view = null; shake = 0; scorePulse = [0, 0]; lastScores = [0, 0];
    dotnet = dotnetRef; mode = gameMode; localSide = side; powerUps = powerUpInfo;
    latest = null; frame = 0; lastTime = performance.now();
    last = { hit: -1, score: -1, power: -1 };

    canvas.onpointerdown = e => { canvas.setPointerCapture(e.pointerId); setTarget(toGameY(e.clientY)); e.preventDefault(); };
    canvas.onpointermove = e => { if (e.buttons || e.pointerType === "touch") setTarget(toGameY(e.clientY)); e.preventDefault(); };
    canvas.onpointerup = canvas.onpointercancel = () => setTarget(null);

    if (mode === "host") {
        link.onInput = y => dotnet.invokeMethod("SetTarget", 1, y);
        link.onCmd = m => { if (m.type === "restart") dotnet.invokeMethod("Restart"); };
    } else if (mode === "client") {
        link.onState = s => { latest = s; };
    }

    raf = requestAnimationFrame(loop);
}

export function stop() {
    cancelAnimationFrame(raf);
    raf = 0;
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
        if (mode === "host" && (frame++ & 1) === 0) link.sendState(latest);
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

function lerp(a, b, t) { return a + (b - a) * t; }

// Client receives ~30 states/s; smooth them so movement looks like 60+ fps.
function smooth(s, dt) {
    if (!view || mode !== "client") { view = { ...s }; return view; }
    const t = 1 - Math.pow(0.0001, dt); // frame-rate independent easing
    const jump = Math.abs(s.ballX - view.ballX) > 120;
    view = {
        ...s,
        ballX: jump ? s.ballX : lerp(view.ballX, s.ballX, t),
        ballY: jump ? s.ballY : lerp(view.ballY, s.ballY, t),
        leftY: lerp(view.leftY, s.leftY, t), rightY: lerp(view.rightY, s.rightY, t),
        leftH: lerp(view.leftH, s.leftH, t), rightH: lerp(view.rightH, s.rightH, t),
    };
    return view;
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

    // Background
    const g = ctx.createLinearGradient(0, 0, W, H);
    g.addColorStop(0, C.bg1); g.addColorStop(1, C.bg2);
    ctx.fillStyle = g; ctx.fillRect(-20, -20, W + 40, H + 40);

    // Subtle grid
    ctx.strokeStyle = "rgba(255,255,255,0.035)"; ctx.lineWidth = 1;
    for (let x = 0; x <= W; x += 40) { ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, H); ctx.stroke(); }
    for (let y = 0; y <= H; y += 40) { ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(W, y); ctx.stroke(); }

    // Center line + circle
    ctx.strokeStyle = C.line; ctx.lineWidth = 2;
    ctx.setLineDash([6, 10]);
    ctx.beginPath(); ctx.moveTo(W / 2, 0); ctx.lineTo(W / 2, H); ctx.stroke();
    ctx.setLineDash([]);
    ctx.beginPath(); ctx.arc(W / 2, H / 2, 50, 0, Math.PI * 2); ctx.stroke();

    // Scores
    ctx.textAlign = "center"; ctx.textBaseline = "middle";
    [[s.leftScore, W / 2 - 90, 0], [s.rightScore, W / 2 + 90, 1]].forEach(([v, x, i]) => {
        const p = scorePulse[i]; scorePulse[i] = Math.max(0, p - dt * 2);
        ctx.font = `800 ${56 + p * 24}px system-ui, -apple-system, sans-serif`;
        ctx.fillStyle = `rgba(255,255,255,${0.18 + p * 0.6})`;
        ctx.fillText(v, x, 60);
    });

    // Paddles
    paddle(20, s.leftY, s.leftH, 0, s);
    paddle(W - 32, s.rightY, s.rightH, 1, s);

    // Power-up
    if (s.hasPowerUp) {
        const info = powerUps[s.powerUpType] || { color: "#fff", symbol: "?" };
        const cx = s.powerUpX + 15, cy = s.powerUpY + 15;
        const pulse = 1 + Math.sin(now / 180) * 0.1;
        ctx.save();
        ctx.translate(cx, cy);
        ctx.rotate(now / 1200);
        glow(info.color, 24);
        ctx.strokeStyle = info.color; ctx.lineWidth = 2.5;
        ctx.beginPath(); ctx.arc(0, 0, 19 * pulse, 0, Math.PI * 1.4); ctx.stroke();
        ctx.restore();
        glow(info.color, 18);
        ctx.fillStyle = info.color;
        ctx.beginPath(); ctx.arc(cx, cy, 13 * pulse, 0, Math.PI * 2); ctx.fill();
        noGlow();
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
        glow(ballColor, 22);
        ctx.fillStyle = ballColor;
        ctx.beginPath(); ctx.arc(bx, by, 7, 0, Math.PI * 2); ctx.fill();
        noGlow();
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
    glow(color, 20);
    const grad = ctx.createLinearGradient(x, 0, x + 12, 0);
    grad.addColorStop(0, color); grad.addColorStop(1, "#ffffff");
    ctx.fillStyle = grad;
    roundRect(x, cy - h / 2, 12, h, 6);
    ctx.fill();
    noGlow();
}
