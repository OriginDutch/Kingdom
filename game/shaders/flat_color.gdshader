// Single-colour low-poly material for props (trees, rocks, houses).
shader_type spatial;
render_mode cull_back;

uniform vec4 albedo : source_color = vec4(1.0);
uniform float roughness = 0.9;

void fragment() {
	ALBEDO = albedo.rgb;
	ROUGHNESS = roughness;
	vec3 flat_n = normalize(cross(dFdx(VERTEX), dFdy(VERTEX)));
	if (dot(flat_n, NORMAL) < 0.0) {
		flat_n = -flat_n;
	}
	NORMAL = flat_n;
}
