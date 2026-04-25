const canvas = document.getElementById('verity-canvas');
const status = document.getElementById('verity-status');
let lastOverlayUpdate = 0;
canvas.focus();

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
  const browserEntry = exports.Verity.Game.Browser.BrowserEntry;

  try {
    browserEntry.InitializeRuntime();
    browserEntry.ResetInputState();
    setStatus(browserEntry.GetDebugState());
  } catch (error) {
    const message = error?.stack || error?.message || String(error);
    setStatus(`init failed: ${message}`);
    throw error;
  }

  const handleMouseMove = (event) => {
    browserEntry.OnMouseMove(event.offsetX, event.offsetY);
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
