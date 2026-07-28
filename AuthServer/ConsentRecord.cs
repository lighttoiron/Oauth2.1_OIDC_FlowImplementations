// A list of consents that a user has granted to a specific client
record ConsentRecord(
    string Subject,
    string ClientId,
    HashSet<string> GrantedScopes,
    DateTime ConsentedAt
);