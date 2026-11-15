shader_type canvas_item;

// Background color to remove: #003973 = (0, 57, 115) out of 255
uniform vec3 key_color = vec3(0.0, 0.22353, 0.45098);
uniform float tolerance : hint_range(0.0, 0.2) = 0.01;

void fragment() {
	vec4 c = texture(TEXTURE, UV);
	if (distance(c.rgb, key_color) < tolerance) {
		discard;
	}
}