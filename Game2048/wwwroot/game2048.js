// Input for 2048: arrow keys / WASD and swipes. Like pong.js, JS only delivers input; C# owns the game.
let dotnet = null, el = null, start = null;

const keys = {
    ArrowUp: "Up", KeyW: "Up", ArrowDown: "Down", KeyS: "Down",
    ArrowLeft: "Left", KeyA: "Left", ArrowRight: "Right", KeyD: "Right",
};

function onKey(e) {
    const dir = keys[e.code];
    if (!dir || e.target.closest?.("input, textarea")) return;
    e.preventDefault();
    dotnet.invokeMethodAsync("OnMove", dir);
}

function onDown(e) { start = { x: e.clientX, y: e.clientY, id: e.pointerId }; }

function onUp(e) {
    if (!start || start.id !== e.pointerId) return;
    const dx = e.clientX - start.x, dy = e.clientY - start.y;
    start = null;
    if (Math.max(Math.abs(dx), Math.abs(dy)) < 24) return;
    const dir = Math.abs(dx) > Math.abs(dy) ? (dx > 0 ? "Right" : "Left") : (dy > 0 ? "Down" : "Up");
    dotnet.invokeMethodAsync("OnMove", dir);
}

export function init(ref, board) {
    dispose();
    dotnet = ref; el = board;
    window.addEventListener("keydown", onKey);
    el.addEventListener("pointerdown", onDown);
    el.addEventListener("pointerup", onUp);
    el.addEventListener("pointercancel", () => start = null);
}

export function dispose() {
    window.removeEventListener("keydown", onKey);
    el?.removeEventListener("pointerdown", onDown);
    el?.removeEventListener("pointerup", onUp);
    dotnet = null; el = null;
}

export function load(key) { return localStorage.getItem(key); }
export function save(key, value) { localStorage.setItem(key, value); }
