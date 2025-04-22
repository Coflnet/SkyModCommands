VERSION=0.3.0
PACKAGE_NAME=Coflnet.Sky.ModCommands.Client
docker run --rm -v "${PWD}:/local" --network host -u $(id -u ${USER}):$(id -g ${USER})  openapitools/openapi-generator-cli generate \
-i http://localhost:5003/swagger/v1/swagger.json \
-g csharp \
-o /local/out --additional-properties=packageName=$PACKAGE_NAME,packageVersion=$VERSION,licenseId=MIT,targetFramework=net8.0

cd out
sed -i 's/GIT_USER_ID/Coflnet/g' src/$PACKAGE_NAME/$PACKAGE_NAME.csproj
sed -i 's/GIT_REPO_ID/SkyModCommands/g' src/$PACKAGE_NAME/$PACKAGE_NAME.csproj
sed -i 's/>OpenAPI/>Coflnet/g' src/$PACKAGE_NAME/$PACKAGE_NAME.csproj

# Use find to locate all .cs files in the src directory, then use sed to replace HttpClient.BaseAddress.AbsolutePath with ""
# that is currently necessary because this PR introduced a bug https://github.com/OpenAPITools/openapi-generator/issues/19451#issuecomment-2782456493
find src -name "*.cs" -type f -exec sed -i 's/HttpClient\.BaseAddress\.AbsolutePath/""/g' {} \;

dotnet pack
cp src/$PACKAGE_NAME/bin/Release/$PACKAGE_NAME.*.nupkg ..
dotnet nuget push ../$PACKAGE_NAME.$VERSION.nupkg --api-key $NUGET_API_KEY --source "nuget.org" --skip-duplicate

rm -rf *.sln
