const contexts = new Map();
const programs = new Map();
const textures = new Map();
const framebuffers = new Map();
const meshes = new Map();
let nextHandle = 1;
let currentProgramRecord = null;
let currentFramebuffer = undefined;
let currentViewport = null;

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
  const gl = canvas.getContext('webgl2', { alpha: true, antialias: true });
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
    gl.deleteShader(shader);
    throw new Error(info ?? 'Shader compile failed');
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
    throw new Error(info ?? 'Program link failed');
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
  deleteMesh
});
