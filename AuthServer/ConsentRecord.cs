record ConsentRecord(
    string Subject,
    string ClientId,
    HashSet<string> GrantedScopes,
    DateTime ConsentedAt
);