const contexts = new Map();
const programs = new Map();
const textures = new Map();
const framebuffers = new Map();
const meshes = new Map();
let nextHandle = 1;
let currentProgramRecord = null;
let currentFramebuffer = undefined;
let currentViewport = null;
let windowOutputBlitter = null;
const windowOutputUv = new Float32Array([
  -1, -1, 0, 0,
   1, -1, 1, 0,
   1,  1, 1, 1,
  -1,  1, 0, 1
]);

function getContextRecord(handle) {
  const record = contexts.get(handle);
  if (!record) {
    throw new Error(`Unknown graphics context: ${handle}`);
  }

  return record;
}

function allocateHandle(store, value) {
  const handle = nextHandle++;
  store.set(handle, value);
  return handle;
}

function ensureProgram(record) {
  if (currentProgramRecord === record) {
    return;
  }

  record.gl.useProgram(record.program);
  currentProgramRecord = record;
}

export function createContext(canvasId, width, height) {
  const canvas = document.getElementById(canvasId);
  if (!canvas) {
    throw new Error(`Canvas not found: ${canvasId}`);
  }

  canvas.width = width;
  canvas.height = height;
  const gl = canvas.getContext('webgl2', { alpha: true, antialias: false });
  if (!gl) {
    throw new Error('WebGL2 is not available in this browser.');
  }

  gl.enable(gl.BLEND);
  gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
  return allocateHandle(contexts, { canvas, gl });
}

export function getWidth(contextHandle) {
  return getContextRecord(contextHandle).canvas.width;
}

export function getHeight(contextHandle) {
  return getContextRecord(contextHandle).canvas.height;
}

export function setViewport(contextHandle, x, y, width, height) {
  if (currentViewport &&
      currentViewport.x === x &&
      currentViewport.y === y &&
      currentViewport.width === width &&
      currentViewport.height === height) {
    return;
  }

  getContextRecord(contextHandle).gl.viewport(x, y, width, height);
  currentViewport = { x, y, width, height };
}

export function enableScissor(contextHandle) {
  getContextRecord(contextHandle).gl.enable(getContextRecord(contextHandle).gl.SCISSOR_TEST);
}

export function disableScissor(contextHandle) {
  getContextRecord(contextHandle).gl.disable(getContextRecord(contextHandle).gl.SCISSOR_TEST);
}

export function setScissor(contextHandle, x, y, width, height) {
  getContextRecord(contextHandle).gl.scissor(x, y, width, height);
}

export function clear(contextHandle, framebufferHandle, r, g, b, a) {
  const { gl } = getContextRecord(contextHandle);
  const framebuffer = framebufferHandle ? framebuffers.get(framebufferHandle) : null;
  if (currentFramebuffer !== framebuffer) {
    gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer);
    currentFramebuffer = framebuffer;
  }
  gl.clearColor(r, g, b, a);
  gl.clear(gl.COLOR_BUFFER_BIT);
}

function compileShader(gl, type, source) {
  const shader = gl.createShader(type);
  gl.shaderSource(shader, source);
  gl.compileShader(shader);
  if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
    const info = gl.getShaderInfoLog(shader);
    const shaderType = type === gl.VERTEX_SHADER ? 'vertex' : 'fragment';
    const numberedSource = source
      .split('\n')
      .map((line, index) => `${index + 1}: ${line}`)
      .join('\n');
    gl.deleteShader(shader);
    throw new Error(`${shaderType} shader compile failed\n${info ?? 'unknown error'}\n--- source ---\n${numberedSource}`);
  }
  return shader;
}

export function createProgram(contextHandle, vertexSource, fragmentSource) {
  const { gl } = getContextRecord(contextHandle);
  const vs = compileShader(gl, gl.VERTEX_SHADER, vertexSource);
  const fs = compileShader(gl, gl.FRAGMENT_SHADER, fragmentSource);
  const program = gl.createProgram();
  gl.attachShader(program, vs);
  gl.attachShader(program, fs);
  gl.linkProgram(program);
  gl.deleteShader(vs);
  gl.deleteShader(fs);
  if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
    const info = gl.getProgramInfoLog(program);
    gl.deleteProgram(program);
    throw new Error(`program link failed\n${info ?? 'unknown error'}`);
  }
  return allocateHandle(programs, { gl, program, uniforms: new Map(), textureUnits: new Map() });
}

function getUniform(record, name) {
  if (!record.uniforms.has(name)) {
    record.uniforms.set(name, record.gl.getUniformLocation(record.program, name));
  }
  return record.uniforms.get(name);
}

export function setProgramFloat(contextHandle, programHandle, name, value) {
  const record = programs.get(programHandle);
  ensureProgram(record);
  record.gl.uniform1f(getUniform(record, name), value);
}

export function setProgramVec2(contextHandle, programHandle, name, x, y) {
  const record = programs.get(programHandle);
  ensureProgram(record);
  record.gl.uniform2f(getUniform(record, name), x, y);
}

export function setProgramVec3(contextHandle, programHandle, name, x, y, z) {
  const record = programs.get(programHandle);
  ensureProgram(record);
  record.gl.uniform3f(getUniform(record, name), x, y, z);
}

export function setProgramVec4(contextHandle, programHandle, name, x, y, z, w) {
  const record = programs.get(programHandle);
  ensureProgram(record);
  record.gl.uniform4f(getUniform(record, name), x, y, z, w);
}

export function setProgramMat4(contextHandle, programHandle, name,
  m11, m12, m13, m14,
  m21, m22, m23, m24,
  m31, m32, m33, m34,
  m41, m42, m43, m44) {
  const record = programs.get(programHandle);
  ensureProgram(record);
  if (!record.mat4Buffer) {
    record.mat4Buffer = new Float32Array(16);
  }
  const buffer = record.mat4Buffer;
  buffer[0] = m11;
  buffer[1] = m12;
  buffer[2] = m13;
  buffer[3] = m14;
  buffer[4] = m21;
  buffer[5] = m22;
  buffer[6] = m23;
  buffer[7] = m24;
  buffer[8] = m31;
  buffer[9] = m32;
  buffer[10] = m33;
  buffer[11] = m34;
  buffer[12] = m41;
  buffer[13] = m42;
  buffer[14] = m43;
  buffer[15] = m44;
  record.gl.uniformMatrix4fv(getUniform(record, name), false, buffer);
}

export function bindProgramTexture(contextHandle, programHandle, name, textureHandle) {
  const record = programs.get(programHandle);
  const { gl } = record;
  let unit = record.textureUnits.get(name);
  if (unit === undefined) {
    unit = record.textureUnits.size;
    record.textureUnits.set(name, unit);
  }
  ensureProgram(record);
  gl.activeTexture(gl.TEXTURE0 + unit);
  gl.bindTexture(gl.TEXTURE_2D, textures.get(textureHandle).texture);
  gl.uniform1i(getUniform(record, name), unit);
}

export function deleteProgram(contextHandle, programHandle) {
  const record = programs.get(programHandle);
  if (!record) return;
  record.gl.deleteProgram(record.program);
  programs.delete(programHandle);
}

export function createTexture(contextHandle, width, height, linear, pixels) {
  const { gl } = getContextRecord(contextHandle);
  const texture = gl.createTexture();
  gl.bindTexture(gl.TEXTURE_2D, texture);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, linear ? gl.LINEAR : gl.NEAREST);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, linear ? gl.LINEAR : gl.NEAREST);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
  gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
  gl.pixelStorei(gl.UNPACK_ALIGNMENT, 1);
  gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, width, height, 0, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
  return allocateHandle(textures, { gl, texture, width, height });
}

export function deleteTexture(contextHandle, textureHandle) {
  const record = textures.get(textureHandle);
  if (!record) return;
  record.gl.deleteTexture(record.texture);
  textures.delete(textureHandle);
}

export function createFramebuffer(contextHandle, textureHandle) {
  const { gl } = getContextRecord(contextHandle);
  const framebuffer = gl.createFramebuffer();
  gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer);
  if (textureHandle) {
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, textures.get(textureHandle).texture, 0);
  }
  return allocateHandle(framebuffers, framebuffer);
}

export function deleteFramebuffer(contextHandle, framebufferHandle) {
  const framebuffer = framebuffers.get(framebufferHandle);
  if (!framebuffer) return;
  getContextRecord(contextHandle).gl.deleteFramebuffer(framebuffer);
  framebuffers.delete(framebufferHandle);
}

export function createMesh(contextHandle, vertices, indices) {
  const { gl } = getContextRecord(contextHandle);
  const vertexArray = vertices.length === 0 ? [] : vertices.split(',').map(Number);
  const indexArray = indices.length === 0 ? [] : indices.split(',').map(Number);
  const vao = gl.createVertexArray();
  const vbo = gl.createBuffer();
  const ebo = gl.createBuffer();
  gl.bindVertexArray(vao);
  gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
  gl.bufferData(gl.ARRAY_BUFFER, new Float32Array(vertexArray), gl.STATIC_DRAW);
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ebo);
  gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, new Uint16Array(indexArray), gl.STATIC_DRAW);
  const stride = 4 * 4;
  gl.enableVertexAttribArray(0);
  gl.vertexAttribPointer(0, 2, gl.FLOAT, false, stride, 0);
  gl.enableVertexAttribArray(1);
  gl.vertexAttribPointer(1, 2, gl.FLOAT, false, stride, 8);
  gl.bindVertexArray(null);
  return allocateHandle(meshes, { gl, vao, vbo, ebo, indexCount: indexArray.length });
}

export function drawMesh(contextHandle, meshHandle, programHandle, framebufferHandle) {
  const mesh = meshes.get(meshHandle);
  const program = programs.get(programHandle);
  const { gl } = getContextRecord(contextHandle);
  const framebuffer = framebufferHandle ? framebuffers.get(framebufferHandle) : null;
  if (currentFramebuffer !== framebuffer) {
    gl.bindFramebuffer(gl.FRAMEBUFFER, framebuffer);
    currentFramebuffer = framebuffer;
  }
  ensureProgram(program);
  gl.bindVertexArray(mesh.vao);
  gl.drawElements(gl.TRIANGLES, mesh.indexCount, gl.UNSIGNED_SHORT, 0);
  gl.bindVertexArray(null);
}

function ensureScreenCanvasSize(canvas) {
  const width = Math.max(1, Math.floor(window.innerWidth || canvas.clientWidth || canvas.width || 1));
  const height = Math.max(1, Math.floor(window.innerHeight || canvas.clientHeight || canvas.height || 1));
  if (canvas.width !== width) {
    canvas.width = width;
  }
  if (canvas.height !== height) {
    canvas.height = height;
  }
}

function ensureWindowOutputBlitter(gl) {
  if (windowOutputBlitter && windowOutputBlitter.gl === gl) {
    return windowOutputBlitter;
  }

  const vertexSource = `#version 300 es
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;
out vec2 vTexCoord;
void main() {
  vTexCoord = aTexCoord;
  gl_Position = vec4(aPosition, 0.0, 1.0);
}`;
  const fragmentSource = `#version 300 es
precision mediump float;
in vec2 vTexCoord;
uniform sampler2D uTexture;
out vec4 outColor;
void main() {
  outColor = texture(uTexture, vTexCoord);
}`;

  const vs = compileShader(gl, gl.VERTEX_SHADER, vertexSource);
  const fs = compileShader(gl, gl.FRAGMENT_SHADER, fragmentSource);
  const program = gl.createProgram();
  gl.attachShader(program, vs);
  gl.attachShader(program, fs);
  gl.linkProgram(program);
  gl.deleteShader(vs);
  gl.deleteShader(fs);
  if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
    const info = gl.getProgramInfoLog(program);
    gl.deleteProgram(program);
    throw new Error(`window output program link failed\n${info ?? 'unknown error'}`);
  }

  const vao = gl.createVertexArray();
  const vbo = gl.createBuffer();
  const ebo = gl.createBuffer();
  const vertices = windowOutputUv;
  const indices = new Uint16Array([0, 1, 2, 0, 2, 3]);
  gl.bindVertexArray(vao);
  gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
  gl.bufferData(gl.ARRAY_BUFFER, vertices, gl.STATIC_DRAW);
  gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, ebo);
  gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, indices, gl.STATIC_DRAW);
  gl.enableVertexAttribArray(0);
  gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 16, 0);
  gl.enableVertexAttribArray(1);
  gl.vertexAttribPointer(1, 2, gl.FLOAT, false, 16, 8);
  gl.bindVertexArray(null);

  windowOutputBlitter = {
    gl,
    program,
    vao,
    vbo,
    textureUniform: gl.getUniformLocation(program, 'uTexture')
  };
  return windowOutputBlitter;
}

function clearRectTopLeft(gl, canvas, x, y, width, height, color) {
  const left = Math.max(0, Math.floor(x));
  const top = Math.max(0, Math.floor(y));
  const right = Math.min(canvas.width, Math.floor(x + width));
  const bottom = Math.min(canvas.height, Math.floor(y + height));
  const clippedWidth = right - left;
  const clippedHeight = bottom - top;
  if (clippedWidth <= 0 || clippedHeight <= 0) {
    return;
  }

  gl.enable(gl.SCISSOR_TEST);
  gl.scissor(left, canvas.height - bottom, clippedWidth, clippedHeight);
  gl.clearColor(color[0], color[1], color[2], color[3]);
  gl.clear(gl.COLOR_BUFFER_BIT);
}

export function presentWindowOutputsBegin(contextHandle) {
  const { canvas, gl } = getContextRecord(contextHandle);
  document.body.classList.add('multi-window-mode');
  ensureScreenCanvasSize(canvas);
  currentProgramRecord = null;
  currentFramebuffer = null;
  currentViewport = null;
  gl.bindFramebuffer(gl.FRAMEBUFFER, null);
  gl.disable(gl.SCISSOR_TEST);
  gl.viewport(0, 0, canvas.width, canvas.height);
  gl.clearColor(0, 0, 0, 0);
  gl.clear(gl.COLOR_BUFFER_BIT);
}

export function presentWindowOutput(
  contextHandle,
  key,
  title,
  x,
  y,
  width,
  height,
  order,
  group,
  decorated,
  lockPosition,
  lockSize,
  textureHandle) {
  const { canvas, gl } = getContextRecord(contextHandle);
  const textureRecord = textures.get(textureHandle);
  if (!textureRecord) {
    return;
  }

  const left = Math.floor(x);
  const top = Math.floor(y);
  const requestedContentWidth = Math.max(1, Math.floor(width));
  const requestedContentHeight = Math.max(1, Math.floor(height));
  const titleHeight = decorated ? 22 : 0;
  const border = decorated ? 2 : 0;
  const frameWidth = requestedContentWidth + border * 2;
  const frameHeight = requestedContentHeight + titleHeight + border * 2;

  if (decorated) {
    clearRectTopLeft(gl, canvas, left, top, frameWidth, frameHeight, [0.02, 0.022, 0.026, 1]);
    clearRectTopLeft(gl, canvas, left + border, top + border, requestedContentWidth, titleHeight, [0.12, 0.14, 0.16, 1]);
  }

  const contentX = left + border;
  const contentY = top + titleHeight + border;
  const clipLeft = Math.max(0, contentX);
  const clipTop = Math.max(0, contentY);
  const clipRight = Math.min(canvas.width, contentX + requestedContentWidth);
  const clipBottom = Math.min(canvas.height, contentY + requestedContentHeight);
  const contentWidth = clipRight - clipLeft;
  const contentHeight = clipBottom - clipTop;
  if (contentWidth <= 0 || contentHeight <= 0) {
    return;
  }

  const u0 = (clipLeft - contentX) / requestedContentWidth;
  const v0 = (clipTop - contentY) / requestedContentHeight;
  const u1 = (clipRight - contentX) / requestedContentWidth;
  const v1 = (clipBottom - contentY) / requestedContentHeight;

  const blitter = ensureWindowOutputBlitter(gl);
  windowOutputUv[2] = u0;
  windowOutputUv[3] = v0;
  windowOutputUv[6] = u1;
  windowOutputUv[7] = v0;
  windowOutputUv[10] = u1;
  windowOutputUv[11] = v1;
  windowOutputUv[14] = u0;
  windowOutputUv[15] = v1;
  gl.disable(gl.SCISSOR_TEST);
  gl.bindFramebuffer(gl.FRAMEBUFFER, null);
  gl.viewport(clipLeft, canvas.height - clipBottom, contentWidth, contentHeight);
  gl.useProgram(blitter.program);
  gl.bindBuffer(gl.ARRAY_BUFFER, blitter.vbo);
  gl.bufferSubData(gl.ARRAY_BUFFER, 0, windowOutputUv);
  gl.activeTexture(gl.TEXTURE0);
  gl.bindTexture(gl.TEXTURE_2D, textureRecord.texture);
  gl.uniform1i(blitter.textureUniform, 0);
  gl.bindVertexArray(blitter.vao);
  gl.drawElements(gl.TRIANGLES, 6, gl.UNSIGNED_SHORT, 0);
  gl.bindVertexArray(null);

  currentProgramRecord = null;
  currentFramebuffer = null;
  currentViewport = null;
}

export function presentWindowOutputsEnd(contextHandle) {
  const { gl } = getContextRecord(contextHandle);
  gl.disable(gl.SCISSOR_TEST);
  currentProgramRecord = null;
  currentFramebuffer = null;
  currentViewport = null;
}


export function deleteMesh(contextHandle, meshHandle) {
  const mesh = meshes.get(meshHandle);
  if (!mesh) return;
  mesh.gl.deleteBuffer(mesh.vbo);
  mesh.gl.deleteBuffer(mesh.ebo);
  mesh.gl.deleteVertexArray(mesh.vao);
  meshes.delete(meshHandle);
}

Object.assign(globalThis, {
  createContext,
  getWidth,
  getHeight,
  setViewport,
  enableScissor,
  disableScissor,
  setScissor,
  clear,
  createProgram,
  setProgramFloat,
  setProgramVec2,
  setProgramVec3,
  setProgramVec4,
  setProgramMat4,
  bindProgramTexture,
  deleteProgram,
  createTexture,
  deleteTexture,
  createFramebuffer,
  deleteFramebuffer,
  createMesh,
  drawMesh,
  deleteMesh,
  presentWindowOutputsBegin,
  presentWindowOutput,
  presentWindowOutputsEnd
});
