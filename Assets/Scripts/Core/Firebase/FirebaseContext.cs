using System;
using Firebase.Firestore;

public readonly struct FirebaseContext
{
    readonly Func<FirebaseFirestore> firestoreProvider;

    public string EnvId { get; }
    public bool IsValid => !string.IsNullOrEmpty(this.EnvId) && this.firestoreProvider != null;

    internal FirebaseContext(string _envId, Func<FirebaseFirestore> _firestoreProvider)
    {
        this.EnvId = _envId;
        this.firestoreProvider = _firestoreProvider ?? throw new ArgumentNullException(nameof(_firestoreProvider));
    }

    internal FirebaseFirestore GetFirestore()
    {
        if (!this.IsValid) throw new InvalidOperationException("FirebaseContext is not initialized.");
        return this.firestoreProvider();
    }
}
