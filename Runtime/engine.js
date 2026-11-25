 (function () {
  "use strict";

  var canvas = null;
  var ctx = null;
  var paint = null;
  var backend = "WebCPU";
  var width = 0;
  var height = 0;
  var objects = [];
  var scripts = [];
  var mainCamera = null;
  var time = 0;

  var keysDown = {};
  var keysPressed = {};
  var keysReleased = {};
  var buttonsDown = {};
  var buttonsPressed = {};
  var mouseX = 0;
  var mouseY = 0;

  var TRIANGLES = 4;
  var TRIANGLE_FAN = 6;
  var LINES = 1;
  var LINE_LOOP = 3;

  function truthy(value) { return value !== null && value !== undefined && value !== false; }

  window.INPUT = {
    key: function (name) { return !!keysDown[String(name).toLowerCase()]; },
    pressed: function (name) { return !!keysPressed[String(name).toLowerCase()]; },
    released: function (name) { return !!keysReleased[String(name).toLowerCase()]; },
    mouseDown: function (name) { return !!buttonsDown[String(name).toLowerCase()]; },
    click: function (name) { return !!buttonsPressed[String(name).toLowerCase()]; },
    get mouseX() { return mouseX; },
    get mouseY() { return mouseY; },
    get left() { return !!buttonsDown["left"]; },
    get right() { return !!buttonsDown["right"]; },
    get middle() { return !!buttonsDown["middle"]; }
  };

  window.FIND = function (name) {
    for (var i = 0; i < objects.length; i++) {
      if (objects[i].name === name) return objects[i];
    }
    return null;
  };

  function keyName(event) {
    var key = event.key;
    if (key.length === 1) return key.toLowerCase();
    switch (key) {
      case " ": return "space";
      case "ArrowUp": return "up";
      case "ArrowDown": return "down";
      case "ArrowLeft": return "left";
      case "ArrowRight": return "right";
      case "Shift": return "shift";
      case "Control": return "control";
      case "Alt": return "alt";
      case "Escape": return "escape";
      case "Enter": return "enter";
      case "Tab": return "tab";
      case "Backspace": return "backspace";
      default: return key.toLowerCase();
    }
  }

  function buttonName(button) {
    if (button === 0) return "left";
    if (button === 1) return "middle";
    if (button === 2) return "right";
    return "unknown";
  }

  function flatten(nodes, output) {
    for (var i = 0; i < nodes.length; i++) {
      var node = nodes[i];
      Object.defineProperty(node, "scale", {
        configurable: true,
        get: function () { return this.scaleX; },
        set: function (value) { this.scaleX = value; this.scaleY = value; this.scaleZ = value; }
      });
      output.push(node);
      if (node.children) flatten(node.children, output);
    }
  }

  function setupScripts() {
    scripts = [];
    for (var i = 0; i < objects.length; i++) {
      var object = objects[i];
      var components = object.components || [];

      for (var j = 0; j < components.length; j++) {
        var entry = components[j];
        var factory = window.SCRIPTS && window.SCRIPTS[entry.id];
        if (typeof factory !== "function") {
          console.warn("script not found: " + entry.script + " on " + object.name);
          continue;
        }

        var instance = { object: object, name: entry.script, script: factory() };
        scripts.push(instance);
        instance.script.setSelf(object);

        if (typeof instance.script.start === "function") {
          try {
            instance.script.start();
          } catch (error) {
            console.error(object.name + "/" + entry.script + ": " + error.message);
          }
        }
      }
    }
  }

  function update(delta) {
    time += delta;

    for (var i = 0; i < scripts.length; i++) {
      var instance = scripts[i];
      if (typeof instance.script.update !== "function") continue;

      instance.script.setSelf(instance.object);
      if (typeof instance.script.setTime === "function") instance.script.setTime(delta, time);

      try {
        instance.script.update(delta);
      } catch (error) {
        console.error(instance.object.name + "/" + instance.name + ": " + error.message);
        instance.script.update = null;
      }
    }

    keysPressed = {};
    keysReleased = {};
    buttonsPressed = {};
  }

  var images = {};

  function image(asset) {
    if (!asset) return null;
    if (!images[asset]) {
      var element = new Image();
      element.src = "assets/" + asset;
      images[asset] = element;
    }

    var img = images[asset];
    return img.complete && img.naturalWidth > 0 ? img : null;
  }

  var MESH_EXTENSIONS = {
    ".obj": 1, ".fbx": 1, ".gltf": 1, ".glb": 1,
    ".dae": 1, ".3ds": 1, ".stl": 1, ".blend": 1, ".ply": 1
  };

  var MESH_SHADES = (function () {
    var list = [];
    for (var i = 0; i < 16; i++) {
      var value = 48 + i * 11;
      list.push("rgb(" + value + "," + (value + 4) + "," + (value + 12) + ")");
    }
    return list;
  })();

  var MESH_LIGHT = { x: 0.40984, y: 0.71723, z: 0.56353 };

  function meshGeometry(asset) {
    if (!asset) return null;
    var dot = asset.lastIndexOf(".");
    if (dot < 0 || !MESH_EXTENSIONS[asset.substring(dot).toLowerCase()]) return null;

    var meshes = window.MESHES;
    return meshes && meshes[asset] ? meshes[asset] : null;
  }

  function palette(kind) {
    switch (kind) {
      case "Sprite": return ["#C7E05A", "#8FA638", "#5C6E20"];
      case "Model": return ["#8B7BE8", "#6A5FBF", "#4A4290"];
      case "Tileset": return ["#2F5A45", "#244737", "#183228"];
      case "Light": return ["#E8D66A", "#B7A83F", "#8A7E2A"];
      default: return ["#E85B60", "#9C3A3E", "#3C1518"];
    }
  }

  function fillColor(kind) {
    switch (kind) {
      case "Sprite": return "#22C57C";
      case "Model": return "#8B7BE8";
      case "Tileset": return "#24503C";
      default: return "#E5484D";
    }
  }

  function accentColor(kind) {
    switch (kind) {
      case "Sprite": return "#45D893";
      case "Model": return "#A99BF5";
      case "Tileset": return "#3B7A5C";
      default: return "#F06A6E";
    }
  }

  function rgba(css) {
    if (!css) return [1, 1, 1, 1];

    if (css.charAt(0) === "#") {
      var hex = css.substring(1);
      if (hex.length === 3) hex = hex[0] + hex[0] + hex[1] + hex[1] + hex[2] + hex[2];
      if (hex.length < 6) return [1, 1, 1, 1];
      var alpha = hex.length >= 8 ? parseInt(hex.substring(6, 8), 16) / 255 : 1;
      return [
        parseInt(hex.substring(0, 2), 16) / 255,
        parseInt(hex.substring(2, 4), 16) / 255,
        parseInt(hex.substring(4, 6), 16) / 255,
        alpha
      ];
    }

    var match = css.match(/rgba?\(([^)]+)\)/);
    if (!match) return [1, 1, 1, 1];

    var parts = match[1].split(",");
    return [
      parseFloat(parts[0]) / 255,
      parseFloat(parts[1]) / 255,
      parseFloat(parts[2]) / 255,
      parts.length > 3 ? parseFloat(parts[3]) : 1
    ];
  }

  function createCpuPainter(context) {
    var viewWidth = 1;
    var viewHeight = 1;

    return {
      resize: function (w, h) { viewWidth = w; viewHeight = h; },

      begin: function () {
        context.fillStyle = "#080808";
        context.fillRect(0, 0, viewWidth, viewHeight);
      },

      line: function (x1, y1, x2, y2, color) {
        context.strokeStyle = color;
        context.lineWidth = 1;
        context.beginPath();
        context.moveTo(x1, y1);
        context.lineTo(x2, y2);
        context.stroke();
      },

      rect: function (cx, cy, w, h, angle, fill, stroke) {
        context.save();
        context.translate(cx, cy);
        if (angle) context.rotate(angle);

        if (fill) {
          context.fillStyle = fill;
          context.fillRect(-w / 2, -h / 2, w, h);
        }

        if (stroke) {
          context.strokeStyle = stroke;
          context.lineWidth = 1;
          context.strokeRect(-w / 2, -h / 2, w, h);
        }

        context.restore();
      },

      circle: function (cx, cy, r, fill, stroke) {
        context.beginPath();
        context.arc(cx, cy, r, 0, Math.PI * 2);

        if (fill) {
          context.fillStyle = fill;
          context.fill();
        }

        if (stroke) {
          context.strokeStyle = stroke;
          context.lineWidth = 1;
          context.stroke();
        }
      },

      sprite: function (img, cx, cy, w, h, angle) {
        context.save();
        context.translate(cx, cy);
        if (angle) context.rotate(angle);
        context.drawImage(img, -w / 2, -h / 2, w, h);
        context.restore();
      },

      polygon: function (points, fill, stroke) {
        context.beginPath();
        context.moveTo(points[0].x, points[0].y);
        for (var i = 1; i < points.length; i++) context.lineTo(points[i].x, points[i].y);
        context.closePath();

        if (fill) {
          context.fillStyle = fill;
          context.fill();
        }

        if (stroke) {
          context.strokeStyle = stroke;
          context.lineWidth = 1;
          context.stroke();
        }
      },

      end: function () {}
    };
  }

  function compileShader(context, type, source) {
    var shader = context.createShader(type);
    context.shaderSource(shader, source);
    context.compileShader(shader);
    if (!context.getShaderParameter(shader, context.COMPILE_STATUS)) {
      context.deleteShader(shader);
      return null;
    }
    return shader;
  }

  function createGlProgram(context) {
    var vertex = compileShader(context, context.VERTEX_SHADER, [
      "attribute vec2 a_position;",
      "attribute vec2 a_uv;",
      "uniform vec2 u_resolution;",
      "varying vec2 v_uv;",
      "void main() {",
      "  gl_Position = vec4(a_position.x / u_resolution.x * 2.0 - 1.0, 1.0 - a_position.y / u_resolution.y * 2.0, 0.0, 1.0);",
      "  v_uv = a_uv;",
      "}"
    ].join("\n"));

    var fragment = compileShader(context, context.FRAGMENT_SHADER, [
      "precision mediump float;",
      "varying vec2 v_uv;",
      "uniform vec4 u_color;",
      "uniform sampler2D u_texture;",
      "uniform float u_textured;",
      "void main() {",
      "  vec4 tex = texture2D(u_texture, v_uv);",
      "  vec3 rgb = mix(u_color.rgb, tex.rgb, u_textured);",
      "  float alpha = u_color.a * mix(1.0, tex.a, u_textured);",
      "  if (alpha < 0.004) discard;",
      "  gl_FragColor = vec4(rgb, alpha);",
      "}"
    ].join("\n"));

    if (!vertex || !fragment) return null;

    var program = context.createProgram();
    context.attachShader(program, vertex);
    context.attachShader(program, fragment);
    context.linkProgram(program);

    if (!context.getProgramParameter(program, context.LINK_STATUS)) {
      context.deleteProgram(program);
      return null;
    }

    return program;
  }

  function createGlPainter(context) {
    var program = createGlProgram(context);
    if (!program) throw new Error("webgl program");

    var buffer = context.createBuffer();
    var white = context.createTexture();
    context.bindTexture(context.TEXTURE_2D, white);
    context.texImage2D(context.TEXTURE_2D, 0, context.RGBA, 1, 1, 0, context.RGBA, context.UNSIGNED_BYTE, new Uint8Array([255, 255, 255, 255]));
    context.texParameteri(context.TEXTURE_2D, context.TEXTURE_MIN_FILTER, context.NEAREST);
    context.texParameteri(context.TEXTURE_2D, context.TEXTURE_MAG_FILTER, context.NEAREST);

    var position = context.getAttribLocation(program, "a_position");
    var uv = context.getAttribLocation(program, "a_uv");
    var resolution = context.getUniformLocation(program, "u_resolution");
    var color = context.getUniformLocation(program, "u_color");
    var textured = context.getUniformLocation(program, "u_textured");
    var sampler = context.getUniformLocation(program, "u_texture");

    context.useProgram(program);
    context.enable(context.BLEND);
    context.blendFunc(context.SRC_ALPHA, context.ONE_MINUS_SRC_ALPHA);
    context.disable(context.DEPTH_TEST);
    context.enableVertexAttribArray(position);
    context.enableVertexAttribArray(uv);
    context.uniform1i(sampler, 0);

    var viewWidth = 1;
    var viewHeight = 1;
    var textures = {};

    function textureFor(img) {
      var key = img.src || "image";
      if (textures[key]) return textures[key];

      var texture = context.createTexture();
      context.bindTexture(context.TEXTURE_2D, texture);
      context.texImage2D(context.TEXTURE_2D, 0, context.RGBA, context.RGBA, context.UNSIGNED_BYTE, img);
      context.texParameteri(context.TEXTURE_2D, context.TEXTURE_MIN_FILTER, context.LINEAR);
      context.texParameteri(context.TEXTURE_2D, context.TEXTURE_MAG_FILTER, context.LINEAR);
      context.texParameteri(context.TEXTURE_2D, context.TEXTURE_WRAP_S, context.CLAMP_TO_EDGE);
      context.texParameteri(context.TEXTURE_2D, context.TEXTURE_WRAP_T, context.CLAMP_TO_EDGE);
      textures[key] = texture;
      return texture;
    }

    function draw(vertices, mode, fill, texture) {
      context.bindBuffer(context.ARRAY_BUFFER, buffer);
      context.bufferData(context.ARRAY_BUFFER, vertices, context.DYNAMIC_DRAW);
      context.vertexAttribPointer(position, 2, context.FLOAT, false, 16, 0);
      context.vertexAttribPointer(uv, 2, context.FLOAT, false, 16, 8);

      var parts = rgba(fill);
      context.uniform4f(color, parts[0], parts[1], parts[2], parts[3]);
      context.uniform1f(textured, texture ? 1 : 0);
      context.bindTexture(context.TEXTURE_2D, texture || white);
      context.drawArrays(mode, 0, vertices.length / 4);
    }

    function quad(cx, cy, w, h, angle) {
      var cos = Math.cos(angle || 0);
      var sin = Math.sin(angle || 0);
      var hw = w / 2;
      var hh = h / 2;

      function point(x, y, u, v) {
        return [cx + x * cos - y * sin, cy + x * sin + y * cos, u, v];
      }

      var a = point(-hw, -hh, 0, 1);
      var b = point(hw, -hh, 1, 1);
      var d = point(hw, hh, 1, 0);
      var e = point(-hw, hh, 0, 0);

      return new Float32Array([
        a[0], a[1], a[2], a[3],
        b[0], b[1], b[2], b[3],
        d[0], d[1], d[2], d[3],
        a[0], a[1], a[2], a[3],
        d[0], d[1], d[2], d[3],
        e[0], e[1], e[2], e[3]
      ]);
    }

    function outline(points) {
      var values = [];
      for (var i = 0; i < points.length; i++) {
        values.push(points[i].x, points[i].y, 0, 0);
      }
      return new Float32Array(values);
    }

    function fan(points) {
      var values = [];
      for (var i = 0; i < points.length; i++) {
        values.push(points[i].x, points[i].y, 0, 0);
      }
      return new Float32Array(values);
    }

    return {
      resize: function (w, h) {
        viewWidth = w;
        viewHeight = h;
      },

      begin: function () {
        context.viewport(0, 0, context.canvas.width, context.canvas.height);
        context.clearColor(0.0313725, 0.0313725, 0.0313725, 1);
        context.clear(context.COLOR_BUFFER_BIT);
        context.uniform2f(resolution, viewWidth, viewHeight);
      },

      end: function () {
        context.flush();
      },

      line: function (x1, y1, x2, y2, stroke) {
        draw(new Float32Array([x1, y1, 0, 0, x2, y2, 0, 0]), LINES, stroke, null);
      },

      rect: function (cx, cy, w, h, angle, fill, stroke) {
        if (fill) draw(quad(cx, cy, w, h, angle), TRIANGLES, fill, null);

        if (stroke) {
          var cos = Math.cos(angle || 0);
          var sin = Math.sin(angle || 0);
          var hw = w / 2;
          var hh = h / 2;
          var points = [];

          function add(x, y) {
            points.push({ x: cx + x * cos - y * sin, y: cy + x * sin + y * cos });
          }

          add(-hw, -hh);
          add(hw, -hh);
          add(hw, hh);
          add(-hw, hh);
          draw(outline(points), LINE_LOOP, stroke, null);
        }
      },

      circle: function (cx, cy, r, fill, stroke) {
        var points = [];
        var segments = r > 24 ? 48 : 24;
        for (var i = 0; i < segments; i++) {
          var angle = i / segments * Math.PI * 2;
          points.push({ x: cx + Math.cos(angle) * r, y: cy + Math.sin(angle) * r });
        }

        if (fill) draw(fan(points), TRIANGLE_FAN, fill, null);
        if (stroke) draw(outline(points), LINE_LOOP, stroke, null);
      },

      sprite: function (img, cx, cy, w, h, angle) {
        draw(quad(cx, cy, w, h, angle), TRIANGLES, "#FFFFFF", textureFor(img));
      },

      polygon: function (points, fill, stroke) {
        if (fill) draw(fan(points), TRIANGLE_FAN, fill, null);
        if (stroke) draw(outline(points), LINE_LOOP, stroke, null);
      }
    };
  }

  var GPU_SHADER = [
    "struct VertexOut {",
    "  @builtin(position) position: vec4<f32>,",
    "  @location(0) uv: vec2<f32>,",
    "  @location(1) color: vec4<f32>,",
    "};",
    "",
    "@vertex",
    "fn vertexMain(@location(0) position: vec2<f32>, @location(1) uv: vec2<f32>, @location(2) color: vec4<f32>) -> VertexOut {",
    "  var out: VertexOut;",
    "  out.position = vec4<f32>(position, 0.0, 1.0);",
    "  out.uv = uv;",
    "  out.color = color;",
    "  return out;",
    "}",
    "",
    "@group(0) @binding(0) var textureSampler: sampler;",
    "@group(0) @binding(1) var textureImage: texture_2d<f32>;",
    "",
    "@fragment",
    "fn fragmentMain(input: VertexOut) -> @location(0) vec4<f32> {",
    "  let texel = textureSample(textureImage, textureSampler, input.uv);",
    "  let alpha = input.color.a * texel.a;",
    "  if (alpha < 0.004) {",
    "    discard;",
    "  }",
    "  return vec4<f32>(input.color.rgb * texel.rgb, alpha);",
    "}"
  ].join("\n");

  async function createGpuDevice() {
    if (!navigator.gpu) return null;

    try {
      var adapter = await navigator.gpu.requestAdapter();
      if (!adapter) return null;

      var device = await adapter.requestDevice();
      return { device: device, format: navigator.gpu.getPreferredCanvasFormat() };
    } catch (error) {
      return null;
    }
  }

  function createGpuResources(device, format) {
    var module = device.createShaderModule({ code: GPU_SHADER });
    var bindGroupLayout = device.createBindGroupLayout({
      entries: [
        { binding: 0, visibility: GPUShaderStage.FRAGMENT, sampler: { type: "filtering" } },
        { binding: 1, visibility: GPUShaderStage.FRAGMENT, texture: { sampleType: "float" } }
      ]
    });
    var pipelineLayout = device.createPipelineLayout({ bindGroupLayouts: [bindGroupLayout] });

    function pipeline(topology) {
      return device.createRenderPipeline({
        layout: pipelineLayout,
        vertex: {
          module: module,
          entryPoint: "vertexMain",
          buffers: [{
            arrayStride: 32,
            attributes: [
              { shaderLocation: 0, offset: 0, format: "float32x2" },
              { shaderLocation: 1, offset: 8, format: "float32x2" },
              { shaderLocation: 2, offset: 16, format: "float32x4" }
            ]
          }]
        },
        fragment: {
          module: module,
          entryPoint: "fragmentMain",
          targets: [{
            format: format,
            blend: {
              color: { srcFactor: "src-alpha", dstFactor: "one-minus-src-alpha", operation: "add" },
              alpha: { srcFactor: "one", dstFactor: "one-minus-src-alpha", operation: "add" }
            }
          }]
        },
        primitive: { topology: topology }
      });
    }

    var sampler = device.createSampler({
      magFilter: "linear",
      minFilter: "linear",
      addressModeU: "clamp-to-edge",
      addressModeV: "clamp-to-edge"
    });

    var white = device.createTexture({
      size: [1, 1, 1],
      format: "rgba8unorm",
      usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST
    });
    device.queue.writeTexture({ texture: white }, new Uint8Array([255, 255, 255, 255]), { bytesPerRow: 4 }, { width: 1, height: 1 });

    var whiteGroup = device.createBindGroup({
      layout: bindGroupLayout,
      entries: [
        { binding: 0, resource: sampler },
        { binding: 1, resource: white.createView() }
      ]
    });

    return {
      triangles: pipeline("triangle-list"),
      lines: pipeline("line-list"),
      bindGroupLayout: bindGroupLayout,
      sampler: sampler,
      whiteGroup: whiteGroup
    };
  }

  function createGpuPainter(canvasElement, device, format) {
    var resources = createGpuResources(device, format);

    var context = canvasElement.getContext("webgpu");
    if (!context) throw new Error("webgpu context");
    context.configure({ device: device, format: format, alphaMode: "opaque" });

    var viewWidth = 1;
    var viewHeight = 1;
    var encoder = null;
    var pass = null;
    var group = resources.whiteGroup;
    var textures = {};
    var pool = [];
    var used = 0;

    function clipX(x) { return x / viewWidth * 2 - 1; }
    function clipY(y) { return 1 - y / viewHeight * 2; }

    function bindGroupFor(img) {
      var key = img.src || "image";
      if (textures[key]) return textures[key];

      var imageWidth = img.naturalWidth || img.width;
      var imageHeight = img.naturalHeight || img.height;
      var texture = device.createTexture({
        size: [imageWidth, imageHeight, 1],
        format: "rgba8unorm",
        usage: GPUTextureUsage.TEXTURE_BINDING | GPUTextureUsage.COPY_DST | GPUTextureUsage.RENDER_ATTACHMENT
      });
      device.queue.copyExternalImageToTexture({ source: img }, { texture: texture }, [imageWidth, imageHeight]);

      var bindGroup = device.createBindGroup({
        layout: resources.bindGroupLayout,
        entries: [
          { binding: 0, resource: resources.sampler },
          { binding: 1, resource: texture.createView() }
        ]
      });
      textures[key] = bindGroup;
      return bindGroup;
    }

    function bufferFor(byteLength) {
      var buffer = pool[used];
      if (!buffer || buffer.size < byteLength) {
        buffer = device.createBuffer({
          size: Math.max(256, byteLength),
          usage: GPUBufferUsage.VERTEX | GPUBufferUsage.COPY_DST
        });
        pool[used] = buffer;
      }
      used++;
      return buffer;
    }

    function draw(vertices, lines) {
      if (!pass || vertices.length === 0) return;

      var buffer = bufferFor(vertices.byteLength);
      device.queue.writeBuffer(buffer, 0, vertices);
      pass.setPipeline(lines ? resources.lines : resources.triangles);
      pass.setBindGroup(0, group);
      pass.setVertexBuffer(0, buffer);
      pass.draw(vertices.length / 8);
    }

    function vertex(values, x, y, u, v, color) {
      values.push(clipX(x), clipY(y), u, v, color[0], color[1], color[2], color[3]);
    }

    function quad(cx, cy, w, h, angle, color) {
      var cos = Math.cos(angle || 0);
      var sin = Math.sin(angle || 0);
      var hw = w / 2;
      var hh = h / 2;
      var values = [];

      function point(x, y, u, v) {
        vertex(values, cx + x * cos - y * sin, cy + x * sin + y * cos, u, v, color);
      }

      point(-hw, -hh, 0, 0);
      point(hw, -hh, 1, 0);
      point(hw, hh, 1, 1);
      point(-hw, -hh, 0, 0);
      point(hw, hh, 1, 1);
      point(-hw, hh, 0, 1);

      return new Float32Array(values);
    }

    function fan(points, color) {
      var values = [];
      for (var i = 1; i < points.length - 1; i++) {
        vertex(values, points[0].x, points[0].y, 0, 0, color);
        vertex(values, points[i].x, points[i].y, 0, 0, color);
        vertex(values, points[i + 1].x, points[i + 1].y, 0, 0, color);
      }
      return new Float32Array(values);
    }

    function outline(points, color) {
      var values = [];
      for (var i = 0; i < points.length; i++) {
        var next = points[(i + 1) % points.length];
        vertex(values, points[i].x, points[i].y, 0, 0, color);
        vertex(values, next.x, next.y, 0, 0, color);
      }
      return new Float32Array(values);
    }

    return {
      resize: function (w, h) { viewWidth = w; viewHeight = h; },

      begin: function () {
        used = 0;
        group = resources.whiteGroup;
        encoder = device.createCommandEncoder();
        pass = encoder.beginRenderPass({
          colorAttachments: [{
            view: context.getCurrentTexture().createView(),
            clearValue: { r: 0.0313725, g: 0.0313725, b: 0.0313725, a: 1 },
            loadOp: "clear",
            storeOp: "store"
          }]
        });
      },

      end: function () {
        if (!pass) return;
        pass.end();
        device.queue.submit([encoder.finish()]);
        pass = null;
        encoder = null;
      },

      ready: function () { return device.queue.onSubmittedWorkDone(); },

      line: function (x1, y1, x2, y2, stroke) {
        var values = [];
        var color = rgba(stroke);
        vertex(values, x1, y1, 0, 0, color);
        vertex(values, x2, y2, 0, 0, color);
        draw(new Float32Array(values), true);
      },

      rect: function (cx, cy, w, h, angle, fill, stroke) {
        if (fill) draw(quad(cx, cy, w, h, angle, rgba(fill)), false);

        if (stroke) {
          var cos = Math.cos(angle || 0);
          var sin = Math.sin(angle || 0);
          var hw = w / 2;
          var hh = h / 2;
          var points = [];

          function add(x, y) {
            points.push({ x: cx + x * cos - y * sin, y: cy + x * sin + y * cos });
          }

          add(-hw, -hh);
          add(hw, -hh);
          add(hw, hh);
          add(-hw, hh);
          draw(outline(points, rgba(stroke)), true);
        }
      },

      circle: function (cx, cy, r, fill, stroke) {
        var points = [];
        var segments = r > 24 ? 48 : 24;
        for (var i = 0; i < segments; i++) {
          var step = i / segments * Math.PI * 2;
          points.push({ x: cx + Math.cos(step) * r, y: cy + Math.sin(step) * r });
        }

        if (fill) draw(fan(points, rgba(fill)), false);
        if (stroke) draw(outline(points, rgba(stroke)), true);
      },

      sprite: function (img, cx, cy, w, h, angle) {
        group = bindGroupFor(img);
        draw(quad(cx, cy, w, h, angle, [1, 1, 1, 1]), false);
        group = resources.whiteGroup;
      },

      polygon: function (points, fill, stroke) {
        if (fill) draw(fan(points, rgba(fill)), false);
        if (stroke) draw(outline(points, rgba(stroke)), true);
      }
    };
  }

  function benchmarkPainter(painter) {
    var start = performance.now();
    painter.begin();

    for (var i = 0; i < 1200; i++) {
      painter.rect(256, 256, 40 + (i % 60), 40 + (i % 90), 0, i % 3 ? "#4488CC" : "rgba(200, 120, 80, 0.5)", null);
      if (i % 5 === 0) painter.circle(256, 256, 60, "rgba(180, 220, 90, 0.3)", null);
    }

    painter.end();
    return performance.now() - start;
  }

  function benchmarkGl() {
    var probe = document.createElement("canvas");
    probe.width = 512;
    probe.height = 512;
    var context = probe.getContext("webgl", { alpha: false }) || probe.getContext("experimental-webgl");
    if (!context) return null;

    var painter = createGlPainter(context);
    painter.resize(512, 512);
    var time = benchmarkPainter(painter);
    context.finish();
    return time;
  }

  function benchmarkCpu() {
    var probe = document.createElement("canvas");
    probe.width = 512;
    probe.height = 512;
    var painter = createCpuPainter(probe.getContext("2d"));
    painter.resize(512, 512);
    return benchmarkPainter(painter);
  }

  async function benchmarkGpu(gpu) {
    var probe = document.createElement("canvas");
    probe.width = 512;
    probe.height = 512;
    var painter = createGpuPainter(probe, gpu.device, gpu.format);
    painter.resize(512, 512);

    painter.begin();
    painter.rect(256, 256, 48, 48, 0, "#4488CC", null);
    painter.end();
    await painter.ready();

    var start = performance.now();
    painter.begin();

    for (var i = 0; i < 1200; i++) {
      painter.rect(256, 256, 40 + (i % 60), 40 + (i % 90), 0, i % 3 ? "#4488CC" : "rgba(200, 120, 80, 0.5)", null);
      if (i % 5 === 0) painter.circle(256, 256, 60, "rgba(180, 220, 90, 0.3)", null);
    }

    painter.end();
    await painter.ready();
    return performance.now() - start;
  }

  function resizeCanvas() {
    var ratio = window.devicePixelRatio || 1;
    width = canvas.clientWidth || window.innerWidth;
    height = canvas.clientHeight || window.innerHeight;
    canvas.width = Math.max(1, Math.round(width * ratio));
    canvas.height = Math.max(1, Math.round(height * ratio));

    if (ctx) ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
  }

  function resize() {
    resizeCanvas();
    if (paint) paint.resize(width, height);
  }

  function render2D() {
    var scale = Math.min(width / 24, height / 14);
    var originX = width / 2 - (mainCamera ? mainCamera.x : 0) * scale;
    var originY = height / 2 - (mainCamera ? mainCamera.y : 0) * scale;
    var left = originX - 12 * scale;
    var top = originY - 7 * scale;

    paint.begin();
    paint.rect(originX, originY, 24 * scale, 14 * scale, 0, "#0B0B0F", null);

    var ordered = objects.slice().sort(function (a, b) { return a.y - b.y; });

    for (var index = 0; index < ordered.length; index++) {
      var node = ordered[index];
      if (!node.visible) continue;

      var centerX = originX + node.x * scale;
      var centerY = originY + node.y * scale;
      var sizeX = Math.max(6, scale * 1.1 * Math.max(0.1, node.scaleX));
      var sizeY = Math.max(6, scale * 1.1 * Math.max(0.1, node.scaleY));
      var size = Math.max(sizeX, sizeY);

      if (node.kind === "Light") {
        paint.circle(centerX, centerY, size * 1.8, "rgba(183, 211, 74, 0.16)", null);
        paint.circle(centerX, centerY, size * 0.16, "#B7D34A", null);
        continue;
      }

      if (node.kind === "Audio") {
        paint.circle(centerX, centerY, size * 0.5, "rgba(138, 138, 138, 0.16)", null);
        continue;
      }

      if (node.kind === "Camera") {
        paint.rect(centerX, centerY, sizeX * 2, sizeY * 1.5, 0, null, "#4E79D5");
        continue;
      }

      if (node.kind === "Tileset") {
        var tileWidth = Math.max(8, scale * 4 * Math.max(0.1, node.scaleX));
        var tileHeight = Math.max(8, scale * 4 * Math.max(0.1, node.scaleY));
        paint.rect(centerX, centerY, tileWidth, tileHeight, 0, "#14201A", "#1F3A2C");
        continue;
      }

      if (node.kind === "Group") continue;

      var angle = (node.rotationZ || 0) * Math.PI / 180;
      var sprite = image(node.asset);

      if (sprite) {
        paint.sprite(sprite, centerX, centerY, sizeX, sizeY, angle);
        continue;
      }

      paint.rect(centerX, centerY, sizeX, sizeY, angle, fillColor(node.kind), "#0B0B0B");

      var accentHeight = sizeY * 0.24;
      var offset = -sizeY / 2 + accentHeight / 2;
      paint.rect(
        centerX - offset * Math.sin(angle),
        centerY + offset * Math.cos(angle),
        sizeX,
        accentHeight,
        angle,
        accentColor(node.kind),
        null);
    }

    paint.end();
  }

  function render3D() {
    var camera = mainCamera
      ? { x: mainCamera.x, y: mainCamera.y, z: mainCamera.z }
      : { x: 0, y: 6.5, z: 12 };

    var fov = mainCamera && mainCamera.fov > 0 ? Math.min(179, Math.max(1, mainCamera.fov)) : 50;
    var fovAxis = mainCamera ? mainCamera.fovAxis : "Vertical";
    var orthographic = !!(mainCamera && mainCamera.projection === "Orthographic");
    var span = fovAxis === "Horizontal"
      ? width
      : fovAxis === "Diagonal"
        ? Math.sqrt(width * width + height * height)
        : height;
    var focal = span / (2 * Math.tan(fov * Math.PI / 360));

    var forward;
    var right;
    var up;

    if (mainCamera) {
      var rotation = {
        x: ((mainCamera.rotationX || 0) + (mainCamera.pitch || 0)) * Math.PI / 180,
        y: ((mainCamera.rotationY || 0) + (mainCamera.yaw || 0)) * Math.PI / 180,
        z: (mainCamera.rotationZ || 0) * Math.PI / 180
      };
      forward = normalize(rotatePoint({ x: 0, y: 0, z: -1 }, rotation));
      right = normalize(cross(forward, rotatePoint({ x: 0, y: 1, z: 0 }, rotation)));
      up = cross(right, forward);
    } else {
      forward = normalize({ x: 0, y: -5.7, z: -12 });
      right = normalize(cross(forward, { x: 0, y: 1, z: 0 }));
      up = cross(right, forward);
    }

    function sub(a, b) { return { x: a.x - b.x, y: a.y - b.y, z: a.z - b.z }; }
    function dot(a, b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
    function cross(a, b) {
      return {
        x: a.y * b.z - a.z * b.y,
        y: a.z * b.x - a.x * b.z,
        z: a.x * b.y - a.y * b.x
      };
    }
    function normalize(v) {
      var length = Math.sqrt(v.x * v.x + v.y * v.y + v.z * v.z) || 1;
      return { x: v.x / length, y: v.y / length, z: v.z / length };
    }

    function project(point) {
      var relative = sub(point, camera);
      var depth = dot(relative, forward);
      if (depth < 0.2) return null;
      var divisor = orthographic ? 10 : depth;
      return {
        x: width / 2 + dot(relative, right) / divisor * focal,
        y: height / 2 - dot(relative, up) / divisor * focal,
        depth: depth
      };
    }

    var faces = [];
    var lines = [];

    function rotatePoint(point, rotation) {
      var cos = Math.cos(rotation.z);
      var sin = Math.sin(rotation.z);
      var x = point.x * cos - point.y * sin;
      var y = point.x * sin + point.y * cos;
      var z = point.z;

      cos = Math.cos(rotation.x);
      sin = Math.sin(rotation.x);
      var y2 = y * cos - z * sin;
      var z2 = y * sin + z * cos;

      cos = Math.cos(rotation.y);
      sin = Math.sin(rotation.y);
      var x3 = x * cos + z2 * sin;
      var z3 = -x * sin + z2 * cos;

      return { x: x3, y: y2, z: z3 };
    }

    function addMesh(flat, node, rotation) {
      var sx = Math.max(0.01, node.scaleX);
      var sy = Math.max(0.01, node.scaleY);
      var sz = Math.max(0.01, node.scaleZ);
      var ox = node.x;
      var oy = node.y;
      var oz = node.z;

      function transform(x, y, z) {
        var point = rotatePoint({ x: x * sx, y: y * sy, z: z * sz }, rotation);
        return { x: point.x + ox, y: point.y + oy, z: point.z + oz };
      }

      for (var i = 0; i + 8 < flat.length; i += 9) {
        var a = transform(flat[i], flat[i + 1], flat[i + 2]);
        var b = transform(flat[i + 3], flat[i + 4], flat[i + 5]);
        var c = transform(flat[i + 6], flat[i + 7], flat[i + 8]);
        var pa = project(a);
        var pb = project(b);
        var pc = project(c);
        if (!pa || !pb || !pc) continue;

        var ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z;
        var vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;
        var nx = uy * vz - uz * vy;
        var ny = uz * vx - ux * vz;
        var nz = ux * vy - uy * vx;
        var length = Math.sqrt(nx * nx + ny * ny + nz * nz);
        if (!(length > 0.000001)) continue;
        nx /= length;
        ny /= length;
        nz /= length;

        var intensity = 0.25 + 0.85 * (nx * MESH_LIGHT.x + ny * MESH_LIGHT.y + nz * MESH_LIGHT.z);
        if (intensity < 0) intensity = 0;
        else if (intensity > 1) intensity = 1;

        faces.push({
          points: [pa, pb, pc],
          depth: (pa.depth + pb.depth + pc.depth) / 3,
          fill: MESH_SHADES[Math.floor(intensity * 15)],
          stroke: null
        });
      }
    }

    function corners(center, size, rotation) {
      var halfX = size.x / 2;
      var halfY = size.y / 2;
      var halfZ = size.z / 2;
      var result = [];

      for (var c = 0; c < 8; c++) {
        var cx = (c === 1 || c === 2 || c === 5 || c === 6) ? halfX : -halfX;
        var cy = c < 4 ? -halfY : halfY;
        var cz = (c === 2 || c === 3 || c === 6 || c === 7) ? halfZ : -halfZ;
        var point = rotatePoint({ x: cx, y: cy, z: cz }, rotation);
        result.push({
          x: center.x + point.x,
          y: center.y + point.y,
          z: center.z + point.z
        });
      }

      return result;
    }

    function addFace(points, fill) {
      var projected = [];
      var depth = 0;

      for (var p = 0; p < points.length; p++) {
        var projectedPoint = project(points[p]);
        if (!projectedPoint) return;
        projected.push(projectedPoint);
        depth += projectedPoint.depth;
      }

      faces.push({ points: projected, depth: depth / points.length, fill: fill, stroke: "#0B0B0B" });
    }

    for (var index = 0; index < objects.length; index++) {
      var node = objects[index];
      if (!node.visible || node.kind === "Group" || node.kind === "Audio") continue;
      if (node === mainCamera) continue;

      var size = {
        x: 1.6 * Math.max(0.1, node.scaleX),
        y: 1.6 * Math.max(0.1, node.scaleY),
        z: 1.6 * Math.max(0.1, node.scaleZ)
      };
      var center = { x: node.x, y: node.y + size.y / 2, z: node.z };
      var rotation = {
        x: node.rotationX * Math.PI / 180,
        y: node.rotationY * Math.PI / 180,
        z: node.rotationZ * Math.PI / 180
      };
      var box = corners(center, size, rotation);

      if (node.kind === "Camera") {
        var edges = [[0,1],[1,2],[2,3],[3,0],[4,5],[5,6],[6,7],[7,4],[0,4],[1,5],[2,6],[3,7]];
        for (var e = 0; e < edges.length; e++) lines.push([box[edges[e][0]], box[edges[e][1]], "#4E79D5"]);
        continue;
      }

      if (node.kind === "Entity" || node.kind === "Model") {
        var mesh = meshGeometry(node.asset);
        if (mesh && mesh.length >= 9) {
          addMesh(mesh, node, rotation);
          continue;
        }
      }

      var colors = palette(node.kind);
      addFace([box[4], box[5], box[6], box[7]], colors[0]);
      addFace([box[0], box[1], box[5], box[4]], colors[1]);
      addFace([box[1], box[2], box[6], box[5]], colors[2]);
      addFace([box[2], box[3], box[7], box[6]], colors[1]);
      addFace([box[3], box[0], box[4], box[7]], colors[2]);
    }

    faces.sort(function (a, b) { return b.depth - a.depth; });

    paint.begin();

    for (var f = 0; f < faces.length; f++) {
      paint.polygon(faces[f].points, faces[f].fill, faces[f].stroke);
    }

    for (var l = 0; l < lines.length; l++) {
      var a = project(lines[l][0]);
      var b = project(lines[l][1]);
      if (!a || !b) continue;
      paint.line(a.x, a.y, b.x, b.y, "#4E79D5");
    }

    paint.end();
  }

  function start() {
    canvas = document.getElementById("game");
    boot();
  }

  async function boot() {
    var requested = window.GAME.renderer || "WebCPU";
    var gpu = null;

    if (requested === "Auto") {
      var best = "WebCPU";
      var bestTime = benchmarkCpu();

      var glTime = null;
      try { glTime = benchmarkGl(); } catch (error) { glTime = null; }
      if (glTime !== null && glTime < bestTime) { best = "WebGL"; bestTime = glTime; }

      gpu = await createGpuDevice();
      if (gpu) {
        var gpuTime = null;
        try { gpuTime = await benchmarkGpu(gpu); } catch (error) { gpuTime = null; }
        if (gpuTime !== null && gpuTime < bestTime) best = "WebGPU";
      }

      requested = best;
    }

    if (requested === "WebGL") {
      var gl = null;
      try {
        gl = canvas.getContext("webgl", { alpha: false, antialias: true }) || canvas.getContext("experimental-webgl");
      } catch (error) {
        gl = null;
      }

      if (gl) {
        try {
          paint = createGlPainter(gl);
          run("WebGL");
          return;
        } catch (error) {
          paint = null;
        }
      }
    }

    if (requested === "WebGPU") {
      if (!gpu) gpu = await createGpuDevice();

      if (gpu) {
        try {
          paint = createGpuPainter(canvas, gpu.device, gpu.format);
          run("WebGPU");
          return;
        } catch (error) {
          paint = null;
        }
      }
    }

    ctx = canvas.getContext("2d");
    if (!ctx) {
      console.error("renderer unavailable: " + requested);
      return;
    }

    paint = createCpuPainter(ctx);
    resizeCanvas();
    run("WebCPU");
  }

  function run(name) {
    backend = name;
    window.BACKEND = backend;

    resize();
    window.addEventListener("resize", resize);

    objects = [];
    flatten(window.GAME.scene || [], objects);

    mainCamera = null;
    if (window.GAME.mainCamera) {
      for (var c = 0; c < objects.length; c++) {
        if (objects[c].kind === "Camera" && objects[c].name === window.GAME.mainCamera) {
          mainCamera = objects[c];
          break;
        }
      }
    }

    setupScripts();

    window.addEventListener("keydown", function (event) {
      var name = keyName(event);
      if (!keysDown[name]) keysPressed[name] = true;
      keysDown[name] = true;
      if (name === "space" || name === "up" || name === "down" || name === "left" || name === "right") {
        event.preventDefault();
      }
    });

    window.addEventListener("keyup", function (event) {
      var name = keyName(event);
      if (keysDown[name]) keysReleased[name] = true;
      keysDown[name] = false;
    });

    canvas.addEventListener("mousemove", function (event) {
      var rect = canvas.getBoundingClientRect();
      mouseX = event.clientX - rect.left;
      mouseY = height - (event.clientY - rect.top);
    });

    canvas.addEventListener("mousedown", function (event) {
      var name = buttonName(event.button);
      if (!buttonsDown[name]) buttonsPressed[name] = true;
      buttonsDown[name] = true;
    });

    canvas.addEventListener("mouseup", function (event) {
      buttonsDown[buttonName(event.button)] = false;
    });

    canvas.addEventListener("mouseleave", function () {
      buttonsDown = {};
    });

    var last = performance.now();

    function frame(now) {
      var delta = Math.min((now - last) / 1000, 0.05);
      last = now;

      if (delta > 0) update(delta);

      if (window.GAME.space === "2D") render2D();
      else render3D();

      requestAnimationFrame(frame);
    }

    requestAnimationFrame(frame);
  }

  window.addEventListener("load", start);
})();
