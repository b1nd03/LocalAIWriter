"""
LocalAI Writer — ONNX Model Conversion Script
Converts a Hugging Face grammar correction model to ONNX format.
"""
import argparse
from pathlib import Path

def convert_model(model_name: str, output_dir: str, quantize: bool = False):
    """Convert a Hugging Face seq2seq model to ONNX."""
    try:
        from transformers import AutoTokenizer, AutoModelForSeq2SeqLM
        from optimum.onnxruntime import ORTModelForSeq2SeqLM
    except ImportError:
        print("ERROR: Install dependencies first: pip install -r requirements.txt")
        return

    output_path = Path(output_dir)
    output_path.mkdir(parents=True, exist_ok=True)

    print(f"Loading model: {model_name}")
    model = ORTModelForSeq2SeqLM.from_pretrained(model_name, export=True)
    tokenizer = AutoTokenizer.from_pretrained(model_name)

    print(f"Saving ONNX model to: {output_path}")
    model.save_pretrained(output_path)
    tokenizer.save_pretrained(output_path)

    # Generate SHA-256 hash
    import hashlib
    onnx_files = list(output_path.glob("*.onnx"))
    for onnx_file in onnx_files:
        sha256 = hashlib.sha256(onnx_file.read_bytes()).hexdigest().upper()
        hash_file = onnx_file.with_suffix(onnx_file.suffix + ".sha256")
        hash_file.write_text(sha256)
        print(f"SHA-256 for {onnx_file.name}: {sha256}")

    print("Conversion complete!")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Convert HF model to ONNX")
    parser.add_argument("--model", default="prithivida/grammar_error_correcter_v1",
                        help="Hugging Face model name")
    parser.add_argument("--output", default="../models",
                        help="Output directory")
    parser.add_argument("--quantize", action="store_true",
                        help="Apply INT8 quantization")
    args = parser.parse_args()
    convert_model(args.model, args.output, args.quantize)
