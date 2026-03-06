"""
LocalAI Writer — Performance Benchmark Suite
Measures inference latency, throughput, and memory usage.
"""
import argparse
import time
import statistics
from pathlib import Path

def run_benchmarks(model_dir: str, iterations: int = 50):
    """Run comprehensive performance benchmarks."""
    try:
        import onnxruntime as ort
        import numpy as np
    except ImportError:
        print("ERROR: pip install onnxruntime numpy")
        return

    model_path = Path(model_dir)
    onnx_files = list(model_path.glob("*.onnx"))
    if not onnx_files:
        print("No ONNX models found")
        return

    for onnx_file in onnx_files:
        print(f"\n{'='*60}")
        print(f"Benchmarking: {onnx_file.name}")
        print(f"{'='*60}")

        session = ort.InferenceSession(str(onnx_file), providers=['CPUExecutionProvider'])

        # Create test inputs of varying lengths
        test_lengths = [8, 16, 32, 64]
        for length in test_lengths:
            inputs = {inp.name: np.zeros(
                [1, length], dtype=np.int64
            ) for inp in session.get_inputs()}

            # Warm-up
            for _ in range(3):
                session.run(None, inputs)

            # Benchmark
            latencies = []
            for _ in range(iterations):
                start = time.perf_counter()
                session.run(None, inputs)
                elapsed = (time.perf_counter() - start) * 1000
                latencies.append(elapsed)

            avg = statistics.mean(latencies)
            p50 = statistics.median(latencies)
            p95 = sorted(latencies)[int(len(latencies) * 0.95)]
            p99 = sorted(latencies)[int(len(latencies) * 0.99)]

            status = "PASS" if avg < 200 else "WARN" if avg < 400 else "FAIL"
            print(f"\n  Sequence length: {length}")
            print(f"    Mean: {avg:.1f}ms | P50: {p50:.1f}ms | P95: {p95:.1f}ms | P99: {p99:.1f}ms [{status}]")

    print(f"\n{'='*60}")
    print("Benchmark complete!")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Run performance benchmarks")
    parser.add_argument("--dir", default="../models", help="Model directory")
    parser.add_argument("--iterations", type=int, default=50, help="Number of iterations")
    args = parser.parse_args()
    run_benchmarks(args.dir, args.iterations)
