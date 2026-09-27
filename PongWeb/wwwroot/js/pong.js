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
    canvas.onpointermove = e => { if (e.buttons || e.pointerType !== "pen") setTarget(toGameY(e.clientY)); e.preventDefault(); };
    canvas.onpointerup = canvas.onpointercancel = e => { if (e.pointerType !== "mouse") setTarget(null); };
    canvas.onpointerleave = e => { if (e.pointerType === "mouse") setTarget(null); };

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
        ctx.save();
        ctx.translate(w.x + 6, w.y + w.h / 2);
        ctx.rotate(w.angle || 0);
        ctx.globalAlpha = 0.2; ctx.fillStyle = "#38bdf8";
        roundRect(-12, -w.h / 2 - 6, 24, w.h + 12, 10); ctx.fill();
        ctx.globalAlpha = 0.35 + hp * 0.65;
        roundRect(-6, -w.h / 2, 12, w.h, 5); ctx.fill();
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
    s.paddles.forEach((p, i) => paddle(p.x, p.y, p.h, p, i === localSide));

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
        const won = s.winner === localSide % 2;
        droneShow(s.winnerName || (won ? "Jij" : "Winnaar"), won ? C.me : C.them, dt, now);
        ctx.fillStyle = "#fff";
        ctx.font = "800 30px system-ui, sans-serif";
        ctx.fillText(won ? "Jij wint! 🎉" : "Verloren", W / 2, H - 70);
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
    if (!drones || drones.name !== name) {
        const text = nameTargets(name);
        const n = text.length;
        const cup = trophyTargets(n);
        drones = {
            name, start: now,
            list: text.map((t, i) => ({
                x: W * (0.1 + 0.8 * Math.random()), y: H + 20 + Math.random() * 60,
                tx: t.x, ty: t.y, cx: cup[i].x, cy: cup[i].y,
                a: (i / n) * Math.PI * 2, r: 60 + (i % 5) * 22,
                hue: [color, "#fde047", "#ffffff"][i % 3],
            })),
        };
    }
    const t = (now - drones.start) / 1000;
    const sp = {};
    // Timeline (loops after the intro): rise 0-3s · swirl 3-6s · name 6-16s · trophy 16-21s · name again ...
    const loopT = t < 21 ? t : 6 + ((t - 21) % 15);
    const k = 1 - Math.pow(0.12, dt);
    for (const d of drones.list) {
        let gx, gy;
        if (loopT < 3) {                                   // take-off: drift up slowly in a column
            gx = d.x; gy = H * 0.9 - (loopT / 3) * H * 0.3 - (d.r - 60);
        } else if (loopT < 6 || (t >= 21 && loopT < 6.01)) { // spinning rings in the sky
            const a = d.a + loopT * 0.9 * (d.r % 44 ? 1 : -1);
            gx = W / 2 + Math.cos(a) * d.r * 1.6; gy = H * 0.4 + Math.sin(a) * d.r * 0.8;
        } else if (loopT < 16) {                            // the winner's name, drones arrive one by one
            const arrive = (d.a / (Math.PI * 2)) * 3;       // spread arrivals over 3 seconds
            if (loopT - 6 < arrive) { gx = d.x; gy = d.y; }
            else { gx = d.tx; gy = d.ty + Math.sin(now / 500 + d.tx / 50) * 3; }
        } else {                                            // trophy
            gx = d.cx; gy = d.cy + Math.sin(now / 400 + d.cx / 30) * 2;
        }
        d.x += (gx - d.x) * k;
        d.y += (gy - d.y) * k;

        // Colour wave rolling through the formation while the name is up.
        let hue = d.hue;
        if (loopT >= 9 && loopT < 16) {
            const wave = Math.sin(d.tx / 70 - now / 350);
            hue = wave > 0.6 ? "#ffffff" : wave < -0.6 ? "#fde047" : color;
        } else if (loopT >= 16) hue = "#fde047";
        ctx.globalAlpha = 0.65 + 0.35 * Math.sin(now / 220 + d.a * 7);
        drawSprite(sp[hue] ??= glowSprite(hue, 1.8, 4), d.x, d.y);
    }
    ctx.globalAlpha = 1;
    if (loopT > 7 && Math.random() < 0.03)
        burst(W * (0.1 + 0.8 * Math.random()), 30 + Math.random() * 60, ["#22d3ee", "#f472b6", "#fde047"][Math.floor(Math.random() * 3)], 24, 140);
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
