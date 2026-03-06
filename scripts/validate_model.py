"""
LocalAI Writer — Model Validation Script
Validates model integrity and runs test inferences.
"""
import argparse
import hashlib
import time
from pathlib import Path

def validate_model(model_dir: str):
    """Validate model file integrity and run test inference."""
    model_path = Path(model_dir)

    # 1. Check files exist
    onnx_files = list(model_path.glob("*.onnx"))
    if not onnx_files:
        print("FAIL: No ONNX files found")
        return False

    # 2. Verify SHA-256
    for onnx_file in onnx_files:
        hash_file = onnx_file.with_suffix(onnx_file.suffix + ".sha256")
        if hash_file.exists():
            expected = hash_file.read_text().strip().upper()
            actual = hashlib.sha256(onnx_file.read_bytes()).hexdigest().upper()
            if expected == actual:
                print(f"PASS: {onnx_file.name} integrity verified")
            else:
                print(f"FAIL: {onnx_file.name} hash mismatch!")
                return False
        else:
            print(f"WARN: No hash file for {onnx_file.name}")

    # 3. Test inference
    try:
        import onnxruntime as ort
        for onnx_file in onnx_files:
            print(f"Testing {onnx_file.name}...")
            session = ort.InferenceSession(str(onnx_file))
            inputs = {inp.name: __import__('numpy').zeros(
                [1] + [s if isinstance(s, int) else 1 for s in inp.shape[1:]],
                dtype=__import__('numpy').int64
            ) for inp in session.get_inputs()}

            start = time.perf_counter()
            _ = session.run(None, inputs)
            elapsed = (time.perf_counter() - start) * 1000
            print(f"PASS: Inference completed in {elapsed:.1f}ms")
    except Exception as e:
        print(f"FAIL: Inference test failed: {e}")
        return False

    print("\nAll validations passed!")
    return True

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Validate ONNX model")
    parser.add_argument("--dir", default="../models", help="Model directory")
    args = parser.parse_args()
    validate_model(args.dir)
