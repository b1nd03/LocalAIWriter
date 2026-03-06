"""
LocalAI Writer — ONNX Model Quantization Script
Applies INT8 dynamic quantization to reduce model size and improve inference speed.
"""
import argparse
from pathlib import Path

def quantize_model(input_path: str, output_path: str):
    """Apply INT8 dynamic quantization to an ONNX model."""
    try:
        from optimum.onnxruntime import ORTQuantizer
        from optimum.onnxruntime.configuration import AutoQuantizationConfig
    except ImportError:
        print("ERROR: Install dependencies first: pip install -r requirements.txt")
        return

    input_dir = Path(input_path)
    output_dir = Path(output_path)
    output_dir.mkdir(parents=True, exist_ok=True)

    print(f"Quantizing model from: {input_dir}")
    quantizer = ORTQuantizer.from_pretrained(input_dir)
    qconfig = AutoQuantizationConfig.avx512_vnni(is_static=False, per_channel=True)
    quantizer.quantize(save_dir=output_dir, quantization_config=qconfig)

    # Generate SHA-256 hash for quantized model
    import hashlib
    for onnx_file in output_dir.glob("*.onnx"):
        sha256 = hashlib.sha256(onnx_file.read_bytes()).hexdigest().upper()
        hash_file = onnx_file.with_suffix(onnx_file.suffix + ".sha256")
        hash_file.write_text(sha256)
        print(f"SHA-256 for {onnx_file.name}: {sha256}")

    print("Quantization complete!")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Quantize ONNX model")
    parser.add_argument("--input", default="../models", help="Input model directory")
    parser.add_argument("--output", default="../models/quantized", help="Output directory")
    args = parser.parse_args()
    quantize_model(args.input, args.output)
