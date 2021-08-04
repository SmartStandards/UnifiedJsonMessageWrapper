param($installPath, $toolsPath, $package,  $project)

# http://www.marcusoft.net/2011/12/creating-tools-only-nuget-package.html
# https://stackoverflow.com/questions/50908074/how-to-pack-command-line-tools-with-nuget
# https://translate.google.com/translate?sl=zh-CN&tl=en&js=y&prev=_t&hl=zh-CN&ie=UTF-8&u=https%3A%2F%2Fwalterlv.github.io%2Fpost%2Fcreate-a-cross-platform-command-based-nuget-tool.html&edit-text=
# https://stackoverflow.com/questions/19054154/copy-files-to-solution-folder-with-init-ps1-and-nuget
# http://skysigal.com/it/ad/powershell/howto/run_scripts_within_the_package_manager_console/background_info

$solution = Get-Interface $dte.Solution ([EnvDTE80.Solution2])
$solutionFileName = $solution.FullName
$solutionRootDir = (Get-Item $solutionFileName).Directory.fullname
#$projectFileName = $project.FullName
#$solutionRootDir = (Get-Item $projectFileName).Directory.parent.fullname

$deployTarget = "$solutionRootDir\(tools)\"
$pkgBinaries = join-path $installPath 'tools\bin'
$deploySource = join-path $installPath 'tools\(tools)'

if (!(test-path $deployTarget)) {
  mkdir $deployTarget
}

Copy-Item "$deploySource/*" $deployTarget -Recurse -Force

$ttIncludeFile = join-path $deployTarget 'UJMW.ttinclude'

$newInstall = "N"
if (!(test-path $ttIncludeFile)) {
  $newInstall = "Y"
}

$profileDir = $Env:USERPROFILE
$escapedProfileDir = [regex]::escape($profileDir)

#if pkg-path starts with solution-dir (or one above), then use a relative path!
$pkgBinaries = $pkgBinaries -replace [regex]::escape((Get-Item $solutionFileName).Directory.fullname), '.'
$pkgBinaries = $pkgBinaries -replace [regex]::escape((Get-Item $solutionFileName).Directory.parent.fullname), '..'

#if pkg-path starts is within the global directory in userprofile, then use placeholder
$pkgBinaries = $pkgBinaries -replace $escapedProfileDir, '%USERPROFILE%'
$pkgBinaries = $pkgBinaries -replace [regex]::escape('\'), '\\'

# patch the exe-path into the tt-file
(Get-Content $ttIncludeFile) | foreach-object { $_ -replace [regex]::escape('PATH-NOT-CONFIGURED'), $pkgBinaries } | Set-Content $ttIncludeFile

Write-Host "UJMW-Tools installed..."

#if ($newInstall -eq "Y") {

# create the solution items folder if it doesn't exist
$deployFolder = $solution.Projects | where-object { $_.ProjectName -eq "Solution Items" } | select -first 1
if(!$deployFolder) {
    $deployFolder = $solution.AddSolutionFolder("Solution Items")
}

$folderItems = Get-Interface $deployFolder.ProjectItems ([EnvDTE.ProjectItems])

ls $deployTarget | where-object { $_.Name -eq "UJMW.Readme.txt" } | foreach-object { 
    $folderItems.AddFromFile($_.FullName) > $null
} > $null

ls $deployTarget | where-object { $_.Name -eq "UJMW.Sample.tt" } | foreach-object { 
    $folderItems.AddFromFile($_.FullName) > $null
} > $null

#}
