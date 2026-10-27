# Custom Shader Graph compatibility patch

`com.clrain.customshadergraph` is embedded in this project so the fix survives
regeneration of `Library/PackageCache`.

Source: https://github.com/jinhyeonseo01/Unity-CustomShaderGraph-Unlit-With-ShadowLight.git
Package path: `Assets/CustomShaderGraph`, version `1.3.1`.
Upstream commit: `c36207029ba348aa368a2e254c649bc321b9d14b`.

For Unity `6000.6.3f1` / URP `17.6.0`, both the Custom Lit and Custom Unlit
`ShaderGraphCustomGUI.cs` files call `DrawShaderGraphProperties(properties)`.
The installed URP no longer accepts the material argument. All other upstream
package files, including asset GUIDs in `.meta` files, are preserved.

To update this dependency, replace the embedded package deliberately and check
these two calls against the installed URP API again.
