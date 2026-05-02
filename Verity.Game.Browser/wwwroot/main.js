const canvas = document.getElementById('verity-canvas');
const status = document.getElementById('verity-status');
let lastOverlayUpdate = 0;
let useIntegerScaling = false;
let browserEntry = null;
canvas.focus();

function refreshIntegerScaling() {
  if (browserEntry && typeof browserEntry.GetIntegerScaling === 'function') {
    useIntegerScaling = !!browserEntry.GetIntegerScaling();
  }
}

function toCanvasSpace(clientX, clientY) {
  const rect = canvas.getBoundingClientRect();
  const width = Math.max(1, rect.width);
  const height = Math.max(1, rect.height);
  const x = ((clientX - rect.left) / width) * Math.max(1, canvas.width || 1);
  const y = ((clientY - rect.top) / height) * Math.max(1, canvas.height || 1);
  return {
    x: Math.max(0, Math.min(Math.max(1, canvas.width || 1), x)),
    y: Math.max(0, Math.min(Math.max(1, canvas.height || 1), y))
  };
}

function syncCanvasPresentation() {
  refreshIntegerScaling();

  if (document.body.classList.contains('multi-window-mode')) {
    canvas.style.width = '100vw';
    canvas.style.height = '100vh';
    return;
  }

  const renderWidth = Math.max(1, canvas.width || 1);
  const renderHeight = Math.max(1, canvas.height || 1);
  let scale = Math.min(window.innerWidth / renderWidth, window.innerHeight / renderHeight);
  if (useIntegerScaling && scale >= 1) {
    scale = Math.max(1, Math.floor(scale));
  }

  canvas.style.width = `${Math.max(1, Math.floor(renderWidth * scale))}px`;
  canvas.style.height = `${Math.max(1, Math.floor(renderHeight * scale))}px`;
}

window.addEventListener('resize', syncCanvasPresentation);

function setStatus(text) {
  if (status) {
    status.style.display = 'block';
    status.style.maxHeight = '80vh';
    status.style.overflow = 'auto';
    status.textContent = text;
  }
}

function hideStatus() {
  if (status) {
    status.style.display = 'none';
    status.textContent = '';
  }
}

setStatus('booting');

window.addEventListener('error', (event) => {
  setStatus(`error: ${event.message}`);
});

window.addEventListener('unhandledrejection', (event) => {
  const message = event.reason?.stack || event.reason?.message || String(event.reason);
  setStatus(`rejection: ${message}`);
});

async function bootstrap() {
  const { dotnet } = await import('./_framework/dotnet.js');
  const graphics = await import('./graphics.js');
  globalThis.__verityGraphics = graphics;
  Object.assign(globalThis, graphics);
  globalThis.VERITY_BROWSER_MINIMAL = true;
  setStatus(`modules imported: namedDotnet=${typeof dotnet}`);

  let getAssemblyExports;
  let getConfig;
  let runMain;
  setStatus('creating runtime');
  try {
    ({ getAssemblyExports, getConfig, runMain } = await dotnet.withDiagnosticTracing(false).create());
    setStatus('runtime created');
  } catch (error) {
    const message = error?.stack || error?.message || String(error);
    setStatus(`runtime create failed: ${message}`);
    throw error;
  }

  const config = getConfig();
  const exports = await getAssemblyExports(config.mainAssemblyName);
  setStatus('exports loaded');
  browserEntry = exports.Verity.Game.Browser.BrowserEntry;

  try {
    browserEntry.InitializeRuntime();
    browserEntry.ResetInputState();
    syncCanvasPresentation();
    setStatus(browserEntry.GetDebugState());
  } catch (error) {
    const message = error?.stack || error?.message || String(error);
    setStatus(`init failed: ${message}`);
    throw error;
  }

  const handleMouseMove = (event) => {
    const point = toCanvasSpace(event.clientX, event.clientY);
    browserEntry.OnMouseMove(point.x, point.y);
  };

  const handleMouseDown = (event) => {
    browserEntry.OnMouseDown(event.button);
    canvas.focus();
  };

  const handleMouseUp = (event) => {
    browserEntry.OnMouseUp(event.button);
  };

  const handleWheel = (event) => {
    browserEntry.OnMouseWheel(event.deltaY);
    event.preventDefault();
  };

  const handleKeyDown = (event) => {
    browserEntry.OnKeyDown(event.code);
    if (event.code === 'ArrowLeft' || event.code === 'ArrowRight' || event.code === 'Space') {
      event.preventDefault();
    }
  };

  const handleKeyUp = (event) => {
    browserEntry.OnKeyUp(event.code);
    if (event.code === 'ArrowLeft' || event.code === 'ArrowRight' || event.code === 'Space') {
      event.preventDefault();
    }
  };

  canvas.addEventListener('mousemove', handleMouseMove);
  canvas.addEventListener('mousedown', handleMouseDown);
  canvas.addEventListener('mouseup', handleMouseUp);
  canvas.addEventListener('wheel', handleWheel, { passive: false });
  canvas.addEventListener('keydown', handleKeyDown);
  canvas.addEventListener('keyup', handleKeyUp);
  document.addEventListener('keydown', handleKeyDown);
  document.addEventListener('keyup', handleKeyUp);
  window.addEventListener('keydown', handleKeyDown);
  window.addEventListener('keyup', handleKeyUp);

  window.addEventListener('blur', () => {
    browserEntry.ResetInputState();
  });

  await runMain();
  setStatus(browserEntry.GetDebugState());

  function frame() {
    try {
      if (!browserEntry.ShouldClose()) {
        browserEntry.TickFrame();
        const previousIntegerScaling = useIntegerScaling;
        refreshIntegerScaling();
        if (previousIntegerScaling !== useIntegerScaling) {
          syncCanvasPresentation();
        }
        const now = performance.now();
        if (now - lastOverlayUpdate >= 250) {
          setStatus(browserEntry.GetDebugState());
          lastOverlayUpdate = now;
        }
        requestAnimationFrame(frame);
      } else {
        setStatus('closed');
      }
    } catch (error) {
      const message = error?.stack || error?.message || String(error);
      setStatus(`frame failed: ${message}`);
      throw error;
    }
  }

  requestAnimationFrame(frame);
}

bootstrap().catch((error) => {
  const message = error?.stack || error?.message || String(error);
  setStatus(`bootstrap failed: ${message}`);
});
