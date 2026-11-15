const controls = {
  cyclicX: 0,
  cyclicY: 0,
  collective: 0,
  yaw: 0,
};

const pressedKeys = new Set();
const CYCLIC_RETURN_SPEED = 2.0;
let cyclicDragging = false;
let lastServerPacket = null;

const cyclicPad = document.querySelector('#cyclicPad');
const cyclicStick = document.querySelector('#cyclicStick');
const collectiveSlider = document.querySelector('#collectiveSlider');
const yawSlider = document.querySelector('#yawSlider');
const statusDot = document.querySelector('#statusDot');
const statusText = document.querySelector('#statusText');

function clamp(value) {
  return Math.max(-1, Math.min(1, value));
}

function formatAxis(value) {
  return `${value >= 0 ? '+' : ''}${value.toFixed(2)}`;
}

function updateDisplay() {
  cyclicStick.style.left = `${50 + controls.cyclicX * 42}%`;
  cyclicStick.style.top = `${50 - controls.cyclicY * 42}%`;
  document.querySelector('#cyclicReadout').textContent = `X ${formatAxis(controls.cyclicX)} / Y ${formatAxis(controls.cyclicY)}`;
  document.querySelector('#collectiveReadout').textContent = formatAxis(controls.collective);
  document.querySelector('#yawReadout').textContent = formatAxis(controls.yaw);
  collectiveSlider.value = controls.collective;
  yawSlider.value = controls.yaw;

  const packet = {
    cyclicX: Number(controls.cyclicX.toFixed(3)),
    cyclicY: Number(controls.cyclicY.toFixed(3)),
    collective: Number(controls.collective.toFixed(3)),
    yaw: Number(controls.yaw.toFixed(3)),
  };
  document.querySelector('#packetView').textContent = JSON.stringify(packet, null, 2);
}

function applyKeyboard() {
  if (!cyclicDragging) {
    const targetX = (pressedKeys.has('d') ? 1 : 0) - (pressedKeys.has('a') ? 1 : 0);
    const targetY = (pressedKeys.has('w') ? 1 : 0) - (pressedKeys.has('s') ? 1 : 0);
    const step = CYCLIC_RETURN_SPEED * 0.05;

    controls.cyclicX = targetX === 0 ? moveToward(controls.cyclicX, 0, step) : targetX;
    controls.cyclicY = targetY === 0 ? moveToward(controls.cyclicY, 0, step) : targetY;
  }

  if (pressedKeys.has('q')) controls.collective = clamp(controls.collective - 0.025);
  if (pressedKeys.has('e')) controls.collective = clamp(controls.collective + 0.025);

  const yawHeld = pressedKeys.has('j') || pressedKeys.has('l');
  if (yawHeld) {
    controls.yaw = (pressedKeys.has('l') ? 1 : 0) - (pressedKeys.has('j') ? 1 : 0);
  }
}

function moveToward(current, target, maxDelta) {
  if (Math.abs(target - current) <= maxDelta) return target;
  return current + Math.sign(target - current) * maxDelta;
}

function setCyclicFromPointer(event) {
  const rect = cyclicPad.getBoundingClientRect();
  const x = ((event.clientX - rect.left) / rect.width - 0.5) / 0.42;
  const y = (0.5 - (event.clientY - rect.top) / rect.height) / 0.42;
  controls.cyclicX = clamp(x);
  controls.cyclicY = clamp(y);
  updateDisplay();
}

function centerCyclic() {
  controls.cyclicX = 0;
  controls.cyclicY = 0;
  cyclicDragging = false;
  updateDisplay();
}

function releaseCyclic() {
  cyclicDragging = false;
}

async function publishControls() {
  try {
    const response = await fetch('/api/control', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(controls),
    });
    if (!response.ok) throw new Error(`HTTP ${response.status}`);
    lastServerPacket = await response.json();
    statusDot.classList.add('ready');
    statusText.textContent = 'SERVER ONLINE';
    document.querySelector('#sequence').textContent = String(lastServerPacket.sequence).padStart(6, '0');
    document.querySelector('#udpStatus').textContent = lastServerPacket.udpEnabled
      ? `UDP FORWARDING // ${lastServerPacket.udpTarget}`
      : 'LOCAL ONLY // UNITY NOT CONNECTED';
  } catch (error) {
    statusDot.classList.remove('ready');
    statusText.textContent = 'SERVER OFFLINE';
  }
}

async function resetAll() {
  controls.cyclicX = 0;
  controls.cyclicY = 0;
  controls.collective = 0;
  controls.yaw = 0;
  pressedKeys.clear();
  updateDisplay();
  try {
    const response = await fetch('/api/reset', { method: 'POST' });
    lastServerPacket = await response.json();
    document.querySelector('#sequence').textContent = String(lastServerPacket.sequence).padStart(6, '0');
  } catch (error) {
    statusText.textContent = 'SERVER OFFLINE';
  }
}

cyclicPad.addEventListener('pointerdown', (event) => {
  cyclicDragging = true;
  cyclicPad.setPointerCapture(event.pointerId);
  setCyclicFromPointer(event);
});
cyclicPad.addEventListener('pointermove', (event) => {
  if (cyclicDragging) setCyclicFromPointer(event);
});
cyclicPad.addEventListener('pointerup', releaseCyclic);
cyclicPad.addEventListener('pointercancel', releaseCyclic);
document.querySelector('#centerButton').addEventListener('click', centerCyclic);
document.querySelector('#resetButton').addEventListener('click', resetAll);

collectiveSlider.addEventListener('input', (event) => {
  controls.collective = Number(event.target.value);
  updateDisplay();
});
yawSlider.addEventListener('input', (event) => {
  controls.yaw = Number(event.target.value);
  updateDisplay();
});

window.addEventListener('keydown', (event) => {
  const key = event.key.toLowerCase();
  if (['w', 'a', 's', 'd', 'q', 'e', 'j', 'l', ' '].includes(key)) event.preventDefault();
  if (key === ' ') resetAll();
  pressedKeys.add(key);
});
window.addEventListener('keyup', (event) => {
  pressedKeys.delete(event.key.toLowerCase());
  if (['w', 'a', 's', 'd'].includes(event.key.toLowerCase()) && !cyclicDragging) {
    applyKeyboard();
    updateDisplay();
  }
});

setInterval(() => {
  applyKeyboard();
  updateDisplay();
}, 50);

setInterval(publishControls, 50);
updateDisplay();
publishControls();
