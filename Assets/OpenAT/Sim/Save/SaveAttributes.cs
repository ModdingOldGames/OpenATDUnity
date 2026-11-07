using System;

namespace OpenAT.Sim.Save
{
    /// <summary>
    /// Excludes state from save games. On a field: the field is not written; after loading it keeps the
    /// value the freshly constructed object gave it (field initialiser / constructor of the fresh
    /// <see cref="World"/>, or the default when the object had to be created without a constructor).
    /// On a class or struct: values of that type are never written (derived content, caches, machinery);
    /// fields holding them behave as above. Use <c>[field: NotSaved]</c> on auto-properties.
    /// Rebuild anything needed in <see cref="ISaveHooks.OnAfterLoad"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, Inherited = false)]
    public sealed class NotSavedAttribute : Attribute { }

    /// <summary>
    /// Optional hooks for state objects that keep caches, indices or derived content that is not saved
    /// (see <see cref="NotSavedAttribute"/>). Implement explicitly in a <c>*.Save.cs</c> partial file.
    /// </summary>
    public interface ISaveHooks
    {
        /// <summary>Called right before the object's fields are written (once per save).</summary>
        void OnBeforeSave(World w);

        /// <summary>
        /// Called after the whole save has been read, in completion order (an object after the objects
        /// it contains; the <see cref="World"/> last). Every field of every object is set by then.
        /// </summary>
        void OnAfterLoad(World w);
    }

    /// <summary>A save file could not be written or read.</summary>
    public sealed class SaveException : Exception
    {
        public SaveException(string message, Exception inner = null) : base(message, inner) { }
    }
}
