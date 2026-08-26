static class PlayerSaveFirestorePaths
{
    const string SaveDocumentId = "current";

    internal static string Current(string _envId, string _userId)
        => FirebaseRootPath.User(_envId, _userId) + "/save/" + SaveDocumentId;
}
