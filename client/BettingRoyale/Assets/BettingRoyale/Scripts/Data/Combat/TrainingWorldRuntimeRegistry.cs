namespace BettingRoyale.Data.Combat
{
    public static class TrainingWorldRuntimeRegistry
    {
        public static ITrainingWorldRuntime Current { get; private set; }
        public static void Register(ITrainingWorldRuntime world) => Current = world;
        public static void Release(ITrainingWorldRuntime world)
        {
            if (ReferenceEquals(Current, world)) Current = null;
        }
        public static void Clear() => Current = null;
    }
}
