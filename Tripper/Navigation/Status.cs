using System;

namespace Tripper.Navigation
{
    /// <summary>
    /// Represents Detour navigation status flags.
    /// Wrapper around uint status returned by Detour pathfinding operations.
    /// </summary>
    public readonly struct Status : IEquatable<Status>
    {
        private readonly uint _value;

        public Status(uint value)
        {
            _value = value;
        }

        /// <summary>
        /// Gets a value indicating whether the operation failed.
        /// </summary>
        public bool Failed => (_value & DT_FAILURE) != 0;

        /// <summary>
        /// Gets a value indicating whether the operation succeeded.
        /// </summary>
        public bool Succeeded => (_value & DT_SUCCESS) != 0;

        /// <summary>
        /// Gets a value indicating whether the operation is still in progress.
        /// </summary>
        public bool InProgress => (_value & DT_IN_PROGRESS) != 0;

        /// <summary>
        /// Gets a value indicating whether the result is a partial path
        /// (goal polygon was not reached, closest reachable point was used).
        /// </summary>
        public bool IsPartialResult => (_value & DT_PARTIAL_RESULT) != 0;

        /// <summary>
        /// Gets the raw status value.
        /// </summary>
        public uint Value => _value;

        // Detour status flags (from DetourStatus.h)
        private const uint DT_FAILURE = 1u << 31;
        private const uint DT_SUCCESS = 1u << 30;
        private const uint DT_IN_PROGRESS = 1u << 29;
        private const uint DT_STATUS_DETAIL_MASK = 0x0ffffff;
        private const uint DT_WRONG_MAGIC = 1u << 0;
        private const uint DT_WRONG_VERSION = 1u << 1;
        private const uint DT_OUT_OF_MEMORY = 1u << 2;
        private const uint DT_INVALID_PARAM = 1u << 3;
        private const uint DT_BUFFER_TOO_SMALL = 1u << 4;
        private const uint DT_OUT_OF_NODES = 1u << 5;
        private const uint DT_PARTIAL_RESULT = 1u << 6;

        public static Status Failure { get; } = new Status(DT_FAILURE);
        public static Status Success { get; } = new Status(DT_SUCCESS);

        public bool Equals(Status other) => _value == other._value;

        public override bool Equals(object? obj) => obj is Status other && Equals(other);

        public override int GetHashCode() => _value.GetHashCode();

        public override string ToString()
        {
            if (Failed) return "Failed";
            if (Succeeded) return "Success";
            if (InProgress) return "InProgress";
            return $"Status(0x{_value:X})";
        }

        public static bool operator ==(Status left, Status right) => left.Equals(right);
        public static bool operator !=(Status left, Status right) => !left.Equals(right);

        public static implicit operator Status(uint value) => new Status(value);
        public static implicit operator uint(Status status) => status._value;
    }
}
