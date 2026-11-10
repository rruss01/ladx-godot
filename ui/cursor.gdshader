shader_type canvas_item;

// #000080 -> (0, 0, 128) out of 255
const vec3 KEY = vec3(0.0, 0.0, 128.0 / 255.0);
uniform float tolerance : hint_range(0.0, 0.2) = 0.01;

void fragment() {
	vec4 c = texture(TEXTURE, UV);
	if (distance(c.rgb, KEY) < tolerance) {
		discard;
	}
}