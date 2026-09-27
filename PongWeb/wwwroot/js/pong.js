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
    if (latest) draw(latest);
}

function draw(s) {
    if (s.hitSeq !== last.hit) { if (last.hit >= 0) play("hit"); last.hit = s.hitSeq; }
    if (s.scoreSeq !== last.score) { if (last.score >= 0) play("score"); last.score = s.scoreSeq; }
    if (s.powerSeq !== last.power) {
        if (last.power >= 0) { play("power"); powerFlash = { text: s.powerText, until: performance.now() + 1800 }; }
        last.power = s.powerSeq;
    }

    ctx.fillStyle = "#000";
    ctx.fillRect(0, 0, W, H);

    ctx.strokeStyle = "#444";
    ctx.setLineDash([10, 12]);
    ctx.lineWidth = 3;
    ctx.beginPath(); ctx.moveTo(W / 2, 0); ctx.lineTo(W / 2, H); ctx.stroke();
    ctx.setLineDash([]);

    ctx.fillStyle = "#fff";
    ctx.font = "bold 48px monospace";
    ctx.textAlign = "center";
    ctx.fillText(s.leftScore, W / 2 - 80, 60);
    ctx.fillText(s.rightScore, W / 2 + 80, 60);

    paddle(20, s.leftY, s.leftH, s.leftInverted, localSide === 0);
    paddle(W - 32, s.rightY, s.rightH, s.rightInverted, localSide === 1);

    if (s.hasPowerUp) {
        const info = powerUps[s.powerUpType] || { color: "#fff", symbol: "?" };
        const size = 30, cx = s.powerUpX + size / 2, cy = s.powerUpY + size / 2;
        const pulse = 1 + Math.sin(performance.now() / 150) * 0.08;
        ctx.fillStyle = info.color;
        ctx.beginPath(); ctx.arc(cx, cy, size / 2 * pulse, 0, Math.PI * 2); ctx.fill();
        ctx.fillStyle = "#000";
        ctx.font = "bold 20px sans-serif";
        ctx.textBaseline = "middle";
        ctx.fillText(info.symbol, cx, cy + 1);
        ctx.textBaseline = "alphabetic";
    }

    ctx.fillStyle = s.ballSpeedFactor > 1 ? "#ffa500" : "#fff";
    if (!s.serving || Math.floor(performance.now() / 200) % 2 === 0)
        ctx.fillRect(s.ballX, s.ballY, 14, 14);

    if (performance.now() < powerFlash.until) {
        ctx.fillStyle = "rgba(255,255,255,0.85)";
        ctx.font = "bold 22px sans-serif";
        ctx.fillText(powerFlash.text, W / 2, H - 24);
    }

    if (s.winner >= 0) {
        ctx.fillStyle = "rgba(0,0,0,0.6)";
        ctx.fillRect(0, 0, W, H);
        ctx.fillStyle = "#fff";
        ctx.font = "bold 44px sans-serif";
        ctx.fillText(s.winner === localSide ? "Jij wint! 🎉" : "Verloren!", W / 2, H / 2);
    }
}

function paddle(x, cy, h, inverted, isLocal) {
    ctx.fillStyle = inverted ? "#9b59b6" : (isLocal ? "#4fc3f7" : "#fff");
    ctx.fillRect(x, cy - h / 2, 12, h);
}
